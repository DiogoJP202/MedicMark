using System.Text.Json;
using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Auth;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.UI.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
public sealed class ClientSession : IAppSession
{
    private readonly IDbContextFactory<LocalDbContext>? _contextos;
    private readonly LocalDbContext? _emprestado;
    private readonly IServerApi api;
    private readonly ITokenStore tokens;
    private readonly IClock clock;
    private readonly IInstitutionSettingsProvider settings;
    private readonly AuthenticatedSessionState estado;
    private readonly ILogger<ClientSession> logger;
    private readonly OfflineAuthOptions _offline;

    /// <summary>
    /// Modo normal: um contexto por operação. Entrar, escolher o setor e restaurar a sessão são
    /// unidades de trabalho independentes, separadas por minutos ou horas de uso.
    /// Ver docs/DECISIONS.md (D-021).
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public ClientSession(
        IDbContextFactory<LocalDbContext> contextos,
        IServerApi api,
        ITokenStore tokens,
        IClock clock,
        IInstitutionSettingsProvider settings,
        AuthenticatedSessionState estado,
        IOptions<OfflineAuthOptions> offlineOptions,
        ILogger<ClientSession> logger)
        : this(api, tokens, clock, settings, estado, offlineOptions, logger) => _contextos = contextos;

    /// <summary>Modo emprestado: trabalha num contexto já aberto, de quem o criou.</summary>
    public ClientSession(
        LocalDbContext db,
        IServerApi api,
        ITokenStore tokens,
        IClock clock,
        IInstitutionSettingsProvider settings,
        AuthenticatedSessionState estado,
        IOptions<OfflineAuthOptions> offlineOptions,
        ILogger<ClientSession> logger)
        : this(api, tokens, clock, settings, estado, offlineOptions, logger) => _emprestado = db;

    private ClientSession(
        IServerApi api,
        ITokenStore tokens,
        IClock clock,
        IInstitutionSettingsProvider settings,
        AuthenticatedSessionState estado,
        IOptions<OfflineAuthOptions> offlineOptions,
        ILogger<ClientSession> logger)
    {
        this.api = api;
        this.tokens = tokens;
        this.clock = clock;
        this.settings = settings;
        this.estado = estado;
        this.logger = logger;
        _offline = offlineOptions.Value;
    }

    /// <summary>Devolve o contexto a usar e se ele é nosso (e portanto precisa ser descartado).</summary>
    private async Task<(LocalDbContext Db, bool Meu)> AbrirAsync(CancellationToken cancellationToken)
    {
        if (_emprestado is not null)
        {
            return (_emprestado, false);
        }

        return (await _contextos!.CreateDbContextAsync(cancellationToken).ConfigureAwait(false), true);
    }

    private static ValueTask FecharAsync(LocalDbContext db, bool meu) =>
        meu ? db.DisposeAsync() : ValueTask.CompletedTask;

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

    /// <summary>
    /// Detalhe técnico da última falha, quando existir. A tela mostra recolhido: quem está no
    /// plantão não precisa dele, e quem vai resolver o problema não consegue sem ele.
    /// </summary>
    public string? LastSignInErrorDetail { get; private set; }

    public async Task<bool> SignInAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        LastSignInError = null;
        LastSignInErrorDetail = null;

        var normalizado = AppUser.NormalizeUserName(userName ?? string.Empty);

        if (string.IsNullOrWhiteSpace(normalizado) || string.IsNullOrEmpty(password))
        {
            LastSignInError = "Informe usuário e senha.";
            return false;
        }

        try
        {
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
        catch (DbUpdateException excecao)
        {
            // Falha ao GRAVAR no banco do aparelho. Antes esta exceção subia até o topo e derrubava
            // a aplicação inteira ("o aplicativo precisa ser reiniciado"), levando junto a única
            // informação útil: a mensagem do EF é genérica e a causa está na exceção interna.
            RegisterStorageFailure(excecao, normalizado);
            return false;
        }
    }

    /// <summary>
    /// Registra uma falha de gravação local de forma que ela seja diagnosticável sem depurador.
    /// A senha e o verificador nunca entram no log — só o que identifica a causa.
    /// </summary>
    private void RegisterStorageFailure(DbUpdateException excecao, string userName)
    {
        var causa = excecao.InnerException?.Message ?? excecao.Message;

        var entidades = excecao.Entries.Count == 0
            ? "nenhuma entidade identificada"
            : string.Join(", ", excecao.Entries
                .Select(e => $"{e.Metadata.ClrType.Name}/{e.State}")
                .Distinct());

        logger.LogError(
            excecao,
            "Falha ao gravar dados de acesso de {UserName} no banco local. Entidades: {Entidades}. Causa: {Causa}",
            userName,
            entidades,
            causa);

        LastSignInError = "Não foi possível gravar os dados de acesso neste aparelho. "
            + "Suas marcações continuam salvas. Feche e abra o aplicativo e tente de novo.";

        LastSignInErrorDetail = $"{causa} [{entidades}]";
    }

    private async Task<(bool Entrou, bool Recusado, string? Motivo)> SignInOnlineAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await SignInOnlineAsync(db, userName, password, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
    }

    private async Task<(bool Entrou, bool Recusado, string? Motivo)> SignInOnlineAsync(
        LocalDbContext db,
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        var device = await GetDeviceAsync(db, cancellationToken).ConfigureAwait(false);

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
            // Mesmo nome de usuário, outro identificador: o aparelho já entrou em OUTRO servidor
            // — outra máquina de desenvolvimento, um servidor reinstalado, um banco restaurado de
            // backup. Cada servidor gera o seu próprio Id para "admin".
            //
            // O nome é único no banco local, então inserir por cima estourava a restrição e a
            // pessoa ficava trancada para fora, sem outra saída além de reinstalar o aplicativo.
            // O servidor que acabou de autenticar é a autoridade: a credencial antiga sai.
            var homonima = await db.Credentials
                .FirstOrDefaultAsync(c => c.UserName == resposta.User.UserName, cancellationToken)
                .ConfigureAwait(false);

            if (homonima is not null)
            {
                db.Credentials.Remove(homonima);

                // Gravação separada, antes de inserir: o índice único é verificado a cada comando,
                // e remover e inserir no mesmo lote colidiria do mesmo jeito.
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                logger.LogInformation(
                    "Credencial local de {UserName} substituída: o servidor passou a usar outro identificador.",
                    resposta.User.UserName);
            }

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
        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await SignInOfflineAsync(db, userName, password, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
    }

    private async Task<bool> SignInOfflineAsync(LocalDbContext db, string userName, string password, CancellationToken cancellationToken)
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
        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var credencial = await db.Credentials
                .AsNoTracking()
                .OrderByDescending(c => c.LastServerValidationUtc)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (credencial is null || !credencial.IsOfflineAccessValid(clock.UtcNow, settings.Current.OfflineLoginValidity))
            {
                return false;
            }

            Activate(credencial);
            await LoadSectorNameAsync(db, cancellationToken).ConfigureAwait(false);

            return true;
        }
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<SectorSummary>> GetAvailableSectorsAsync(CancellationToken cancellationToken = default)
    {
        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await GetAvailableSectorsAsync(db, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<SectorSummary>> GetAvailableSectorsAsync(LocalDbContext db, CancellationToken cancellationToken)
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

        var (db, meu) = await AbrirAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var device = await GetDeviceAsync(db, cancellationToken).ConfigureAwait(false);
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
        finally
        {
            await FecharAsync(db, meu).ConfigureAwait(false);
        }
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
    private async Task LoadSectorNameAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        var device = await GetDeviceAsync(db, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Sem cache em campo: a entidade pertence ao contexto que a leu, e esse contexto morre no fim
    /// da operação. Guardá-la entre operações era exatamente o tipo de estado velho que a fábrica
    /// de contextos veio eliminar.
    /// </summary>
    private static async Task<DeviceState> GetDeviceAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        var dispositivo = await db.DeviceState.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (dispositivo is null)
        {
            dispositivo = new DeviceState();
            db.DeviceState.Add(dispositivo);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return dispositivo;
    }
}
