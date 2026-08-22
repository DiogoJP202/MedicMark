using System.Net.Http.Json;
using System.Text.Json;
using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Client.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UiResult = ChecklistPlantao.Client.Abstractions.Result;

namespace ChecklistPlantao.Client.Core.Services;

/// <summary>Endereço do servidor e nome do aparelho, guardados no banco local.</summary>
public sealed class ServerConfigurationService(
    IDbContextFactory<LocalDbContext> contextos,
    IServiceProvider services,
    ClientConfigurationDefaults defaults) : IServerConfigurationService, IServerAddressProvider
{
    /// <summary>
    /// Cache de VALORES, não da entidade. Guardar o <see cref="DeviceState"/> rastreado manteria
    /// vivo o contexto que o leu — exatamente o que a fábrica de contextos veio evitar. Estas três
    /// propriedades são síncronas porque quem monta a requisição HTTP precisa do endereço na hora.
    /// </summary>
    private Configuracao? _cache;

    private sealed record Configuracao(string? ServerUrl, string DeviceName, Guid DeviceId);

    public string? ServerUrl => Load().ServerUrl;

    public string DeviceName => Load().DeviceName;

    public Guid DeviceId => Load().DeviceId;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerUrl);

    /// <summary>
    /// <see cref="IServerApi"/> resolvido sob demanda: esta classe atende
    /// <see cref="IServerAddressProvider"/>, de quem o <see cref="HttpServerApi"/> depende no
    /// construtor. Pedi-lo aqui fecharia o ciclo — e como o registro passa por uma fábrica,
    /// <c>ValidateOnBuild</c> não o enxergaria: a falha só apareceria na resolução.
    /// </summary>
    public Task<ServerProbeResponse?> TestConnectionAsync(string url, CancellationToken cancellationToken = default) =>
        services.GetRequiredService<IServerApi>().ProbeAsync(url, cancellationToken);

    public async Task SaveAsync(string url, string deviceName, CancellationToken cancellationToken = default)
    {
        await using var db = await contextos.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var estado = await EnsureAsync(db, cancellationToken).ConfigureAwait(false);
        estado.Configure(HttpServerApi.NormalizeUrl(url), deviceName);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _cache = Instantanea(estado);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextos.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var estado = await EnsureAsync(db, cancellationToken).ConfigureAwait(false);
        if (defaults.ServerUrl is not null)
        {
            estado.Configure(defaults.ServerUrl, estado.DeviceName);
        }
        else
        {
            estado.ClearServer();
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _cache = Instantanea(estado);
    }

    private Configuracao Load()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        using var db = contextos.CreateDbContext();

        return _cache = Instantanea(db.DeviceState.AsNoTracking().FirstOrDefault() ?? new DeviceState());
    }

    private static Configuracao Instantanea(DeviceState estado) =>
        new(estado.ServerUrl, estado.DeviceName, estado.DeviceId);

    private static async Task<DeviceState> EnsureAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        var estado = await db.DeviceState.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (estado is null)
        {
            estado = new DeviceState();
            db.DeviceState.Add(estado);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return estado;
    }
}
