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
    AuthenticatedSessionState estado,
    IOptions<OfflineAuthOptions> offlineOptions,
    ILogger<ClientSession> logger) : IAppSession
{
    private readonly OfflineAuthOptions _offline = offlineOptions.Value;

    private DeviceState? _dispositivo;

    /// <summary>
    /// O evento pertence ao estado compartilhado, não a esta instância: quem assina é a interface,
    /// que pode estar em outro escopo. Ver <see cref="AuthenticatedSessionState"/>.
    /// </summary>
    public event Action? Changed
    {
        add => estado.Changed += value;
        remove => estado.Changed -= value;
    }

    public bool IsAuthenticated => estado.IsAuthenticated;

    public string DisplayName => estado.DisplayName;

    public EffectiveAccess Access => estado.Access;

    public bool PermissionsAreStale
    {
        get
        {
            if (estado.LastServerValidationUtc is not { } validado)
            {
                return false;
            }

            // "Desatualizado" começa na metade da validade offline: avisa antes de expirar,
            // dando tempo de reconectar sem perder o acesso no meio do plantão.
            var validade = settings.Current.OfflineLoginValidity;
            return clock.UtcNow - validado > validade / 2;
        }
    }

    public DateTime? LastServerValidationUtc => estado.LastServerValidationUtc;

    public Guid? CurrentSectorId => estado.CurrentSectorId;

    public string? CurrentSectorName => estado.CurrentSectorName;

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

        var online = await SignInOnlineAsync(normalizado, password, cancellationToken).ConfigureAwait(false);

        if (online.Entrou)
        {
            return true;
        }

        // O servidor respondeu e recusou: a resposta dele é a verdade. Tentar o caminho offline
        // aqui trocaria "sua conta está bloqueada" por "você nunca entrou neste aparelho" — uma
        // mensagem errada, que esconde do usuário exatamente o que ele precisa saber para agir.
        if (online.Recusado)
        {
            LastSignInError = online.Motivo;
            return false;
        }

        return await SignInOfflineAsync(normalizado, password, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(bool Entrou, bool Recusado, string? Motivo)> SignInOnlineAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        var device = await GetDeviceAsync(cancellationToken).ConfigureAwait(false);

        var resultado = await api
            .LoginAsync(new LoginRequest(userName, password, device.DeviceId.ToString(), device.DeviceName), cancellationToken)
            .ConfigureAwait(false);

        if (resultado.WasRefused)
        {
            logger.LogInformation("Servidor recusou a entrada de {UserName}: {Codigo}.", userName, resultado.ErrorCode ?? "sem código");

            return (false, true, resultado.ErrorMessage ?? "Não foi possível entrar. Verifique o usuário e a senha.");
        }

        var resposta = resultado.Response;

        if (resposta is null)
        {
            return (false, false, null);
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

        return (true, false, null);
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

        estado.SignOut();
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

        var visiveis = estado.Access.FilterSectors(setores, s => s.Id).ToList();
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
        if (!estado.Access.CanAccessSector(sectorId))
        {
            return;
        }

        var device = await GetDeviceAsync(cancellationToken).ConfigureAwait(false);
        device.SelectSector(sectorId);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var nome = await db.Sectors
            .AsNoTracking()
            .Where(s => s.Id == sectorId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        estado.SelectSector(sectorId, nome);
    }

    private void Activate(LocalCredential credencial) =>
        estado.SignIn(
            credencial.UserId,
            credencial.UserName,
            credencial.DisplayName,
            BuildAccess(credencial.PermissionsSnapshot),
            credencial.LastServerValidationUtc);

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

    /// <summary>
    /// Recupera o setor escolhido a partir do banco local e o publica no estado compartilhado.
    /// Chamado ao restaurar a sessão, quando o estado em memória ainda está vazio.
    /// </summary>
    private async Task LoadSectorNameAsync(CancellationToken cancellationToken)
    {
        var device = await GetDeviceAsync(cancellationToken).ConfigureAwait(false);

        if (device.CurrentSectorId is not { } id)
        {
            estado.SelectSector(null, null);
            return;
        }

        var nome = await db.Sectors
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        estado.SelectSector(id, nome);
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
