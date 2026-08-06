using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Configuration;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Infrastructure.Settings;

/// <summary>
/// Lê as configurações da tabela <c>Configuracoes</c> e mantém um cache em memória.
/// É singleton: abre o próprio escopo para consultar o banco em vez de segurar um DbContext.
/// A tradução entre texto e tipo forte fica em <see cref="InstitutionSettingsSerializer"/>,
/// compartilhada com o cliente.
/// </summary>
public sealed class DatabaseInstitutionSettingsProvider(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInstitutionSettingsProvider> logger) : IInstitutionSettingsProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InstitutionSettings _current = InstitutionSettings.Default;
    private bool _loaded;

    public InstitutionSettings Current => _current;

    public async ValueTask<InstitutionSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return _current;
        }

        await ReloadAsync(cancellationToken).ConfigureAwait(false);
        return _current;
    }

    public async ValueTask ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var stored = await db.AppSettings
                .AsNoTracking()
                .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, cancellationToken)
                .ConfigureAwait(false);

            _current = InstitutionSettingsSerializer.Materialize(stored, logger);
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
