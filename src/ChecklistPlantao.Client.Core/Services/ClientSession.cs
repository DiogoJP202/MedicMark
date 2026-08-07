using System.Text.Json;
using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Auth;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.UI.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChecklistPlantao.Client.Core.Services;

/// <summary>Snapshot de permissões guardado localmente para o modo offline.</summary>
internal sealed record PermissionsSnapshot(
    IReadOnlyList<string> Permissions,
    IReadOnlyList<Guid> SectorIds,
    bool GrantsAllSectors);

/// <summary>
/// Sessão do usuário no dispositivo.
///
/// Regra do enunciado: o PRIMEIRO acesso de um aparelho exige servidor. A partir daí, a entrada
/// offline funciona por um prazo configurável, validada contra o verificador local — nunca
/// contra o hash do servidor, que jamais chega aqui.
/// </summary>
public sealed class ClientSession(
    LocalDbContext db,
    IServerApi api,
    ITokenStore tokens,
    IClock clock,
    IInstitutionSettingsProvider settings,
    IOptions<OfflineAuthOptions> offlineOptions,
    ILogger<ClientSession> logger) : IAppSession
{
    private readonly OfflineAuthOptions _offline = offlineOptions.Value;

    private LocalCredential? _credencial;
    private EffectiveAccess _acesso = EffectiveAccess.None;
    private DeviceState? _dispositivo;

    public event Action? Changed;

    public bool IsAuthenticated { get; private set; }

    public string DisplayName => _credencial?.DisplayName ?? string.Empty;

    public EffectiveAccess Access => _acesso;

    public bool PermissionsAreStale
    {
        get
        {
            if (_credencial is null)
            {
                return false;
            }

            // "Desatualizado" começa na metade da validade offline: avisa antes de expirar,
            // dando tempo de reconectar sem perder o acesso no meio do plantão.
            var validade = settings.Current.OfflineLoginValidity;
            return clock.UtcNow - _credencial.LastServerValidationUtc > validade / 2;
        }
    }

    public DateTime? LastServerValidationUtc => _credencial?.LastServerValidationUtc;

    public Guid? CurrentSectorId => _dispositivo?.CurrentSectorId;

    public string? CurrentSectorName { get; private set; }

    public string? LastSignInError { get; private set; }

    public async Task<bool> SignInAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        LastSignInError = null;

        var normalizado = AppUser.NormalizeUserName(userName ?? string.Empty);

        if (string.IsNullOrWhiteSpace(normalizado) || string.IsNullOrEmpty(password))
        {
            LastSignInError = "Informe usuário e senha.";
            return false;
        }

        if (api.IsReachable && await SignInOnlineAsync(normalizado, password, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return await SignInOfflineAsync(normalizado, password, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SignInOnlineAsync(string userName, string password, CancellationToken cancellationToken)
    {
        var device = await GetDeviceAsync(cancellationToken).ConfigureAwait(false);

        var resposta = await api
            .LoginAsync(new LoginRequest(userName, password, device.DeviceId.ToString(), device.DeviceName), cancellationToken)
            .ConfigureAwait(false);

        if (resposta is null)
        {
            return false;
        }

        await tokens
            .SaveAsync(resposta.Tokens.AccessToken, resposta.Tokens.AccessTokenExpiresAtUtc, resposta.Tokens.RefreshToken, cancellationToken)
            .ConfigureAwait(false);

        var snapshot = JsonSerializer.Serialize(new PermissionsSnapshot(
            resposta.User.Permissions, resposta.User.SectorIds, resposta.User.GrantsAllSectors));

        var credencial = await db.Credentials.FirstOrDefaultAsync(c => c.UserId == resposta.User.UserId, cancellationToken).ConfigureAwait(false);

        // O verificador offline é derivado AQUI, da senha que o usuário acabou de digitar.
        // O servidor nunca envia hash nenhum.
        var (salt, verifier, iteracoes) = OfflineCredentialFactory.Derive(password, _offline);

        if (credencial is null)
        {
            credencial = new LocalCredential(
                resposta.User.UserId, resposta.User.UserName, resposta.User.DisplayName,
                salt, verifier, iteracoes, snapshot, clock.UtcNow);

            db.Credentials.Add(credencial);
        }
        else
        {
            credencial.UpdateVerifier(salt, verifier, iteracoes);
            credencial.RefreshFromServer(resposta.User.DisplayName, snapshot, clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Activate(credencial);
        logger.LogInformation("Entrada online concluída para {UserId}.", resposta.User.UserId);

        return true;
    }

    private async Task<bool> SignInOfflineAsync(string userName, string password, CancellationToken cancellationToken)
    {
        var credencial = await db.Credentials.FirstOrDefaultAsync(c => c.UserName == userName, cancellationToken).ConfigureAwait(false);

        if (credencial is null)
        {
            LastSignInError = "Este usuário ainda não entrou neste dispositivo. Conecte-se ao servidor para o primeiro acesso.";
            return false;
        }

        var agora = clock.UtcNow;
        var configuracoes = settings.Current;

        if (credencial.IsLocked(agora))
        {
            LastSignInError = "Muitas tentativas neste dispositivo. Aguarde alguns minutos ou conecte-se ao servidor.";
            return false;
        }

        if (!credencial.IsOfflineAccessValid(agora, configuracoes.OfflineLoginValidity))
        {
            LastSignInError = "O acesso offline expirou. Conecte-se ao servidor para entrar novamente.";
            return false;
        }

        if (!OfflineCredentialFactory.Verify(password, credencial))
        {
            credencial.RegisterFailure(configuracoes.OfflineLoginMaxAttempts, agora, TimeSpan.FromMinutes(_offline.LockMinutes));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            LastSignInError = "Usuário ou senha inválidos.";
            return false;
        }

        credencial.RegisterSuccess();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Activate(credencial);
        logger.LogInformation("Entrada offline concluída para {UserId}.", credencial.UserId);

        return true;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await tokens.ClearAsync(cancellationToken).ConfigureAwait(false);

        IsAuthenticated = false;
        _credencial = null;
        _acesso = EffectiveAccess.None;
        CurrentSectorName = null;

        Changed?.Invoke();
    }

    /// <summary>Restaura a sessão ao abrir o aplicativo, sem pedir senha de novo.</summary>
    public async Task<bool> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        var credencial = await db.Credentials
            .OrderByDescending(c => c.LastServerValidationUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (credencial is null || !credencial.IsOfflineAccessValid(clock.UtcNow, settings.Current.OfflineLoginValidity))
        {
            return false;
        }

        Activate(credencial);
        await LoadSectorNameAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<IReadOnlyList<SectorSummary>> GetAvailableSectorsAsync(CancellationToken cancellationToken = default)
    {
        var setores = await db.Sectors
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var visiveis = _acesso.FilterSectors(setores, s => s.Id).ToList();
        var resultado = new List<SectorSummary>(visiveis.Count);

        foreach (var setor in visiveis)
        {
            var sessao = await db.OperationalSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SectorId == setor.Id && s.Status == Domain.Operations.SessionStatus.Open, cancellationToken)
                .ConfigureAwait(false);

            var pendentes = sessao is null
                ? 0
                : await db.ChecklistEntries
                    .AsNoTracking()
                    .CountAsync(e => e.SessionId == sessao.Id && !e.IsCompleted, cancellationToken)
                    .ConfigureAwait(false);

            resultado.Add(new SectorSummary(setor.Id, setor.Name, pendentes, sessao is not null));
        }

        return resultado;
    }

    public async Task SelectSectorAsync(Guid sectorId, CancellationToken cancellationToken = default)
    {
        if (!_acesso.CanAccessSector(sectorId))
        {
            return;
        }

        var device = await GetDeviceAsync(cancellationToken).ConfigureAwait(false);
        device.SelectSector(sectorId);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await LoadSectorNameAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }

    private void Activate(LocalCredential credencial)
    {
        _credencial = credencial;
        _acesso = BuildAccess(credencial.PermissionsSnapshot);
        IsAuthenticated = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// Reconstrói o acesso efetivo a partir do snapshot. Usa um grupo sintético porque
    /// <see cref="EffectiveAccess"/> só sabe somar grupos — a soma já veio pronta do servidor.
    /// </summary>
    private static EffectiveAccess BuildAccess(string snapshotJson)
    {
        PermissionsSnapshot? snapshot;

        try
        {
            snapshot = JsonSerializer.Deserialize<PermissionsSnapshot>(snapshotJson);
        }
        catch (JsonException)
        {
            return EffectiveAccess.None;
        }

        if (snapshot is null)
        {
            return EffectiveAccess.None;
        }

        var grupo = new AccessGroup(Guid.Empty, "snapshot", null, snapshot.GrantsAllSectors, DateTime.UnixEpoch);
        grupo.ReplacePermissions(snapshot.Permissions.Where(Permissions.IsKnown), DateTime.UnixEpoch);
        grupo.ReplaceSectors(snapshot.SectorIds, DateTime.UnixEpoch);

        return EffectiveAccess.FromGroups([grupo]);
    }

    private async Task LoadSectorNameAsync(CancellationToken cancellationToken)
    {
        var device = await GetDeviceAsync(cancellationToken).ConfigureAwait(false);

        CurrentSectorName = device.CurrentSectorId is { } id
            ? await db.Sectors.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
    }

    private async Task<DeviceState> GetDeviceAsync(CancellationToken cancellationToken)
    {
        if (_dispositivo is not null)
        {
            return _dispositivo;
        }

        _dispositivo = await db.DeviceState.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (_dispositivo is null)
        {
            _dispositivo = new DeviceState();
            db.DeviceState.Add(_dispositivo);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return _dispositivo;
    }
}
