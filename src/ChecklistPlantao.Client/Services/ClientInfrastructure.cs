using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Configuration;
using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChecklistPlantao.Client.Services;

/// <summary>
/// Configurações da instituição no dispositivo, lidas da tabela local sincronizada.
///
/// Mesmo contrato do servidor: <see cref="Current"/> é síncrono porque a interface formata
/// horários dentro do render, onde não cabe await.
/// </summary>
public sealed class LocalInstitutionSettingsProvider(
    IServiceProvider services,
    ILogger<LocalInstitutionSettingsProvider> logger) : IInstitutionSettingsProvider
{
    private readonly SemaphoreSlim _porta = new(1, 1);
    private InstitutionSettings _atual = InstitutionSettings.Default;
    private bool _carregado;

    public InstitutionSettings Current => _atual;

    public async ValueTask<InstitutionSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_carregado)
        {
            return _atual;
        }

        await ReloadAsync(cancellationToken).ConfigureAwait(false);
        return _atual;
    }

    public async ValueTask ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _porta.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var escopo = services.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<LocalDbContext>();

            var armazenado = await db.AppSettings
                .AsNoTracking()
                .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, cancellationToken)
                .ConfigureAwait(false);

            _atual = InstitutionSettingsSerializer.Materialize(armazenado, logger);
            _carregado = true;
        }
        finally
        {
            _porta.Release();
        }
    }
}

/// <summary>
/// Fuso da instituição no dispositivo.
///
/// Usa o fuso CONFIGURADO, e não o do aparelho: um celular com fuso errado não pode deslocar
/// os horários do plantão inteiro.
/// </summary>
public sealed class ClientInstitutionTimeZone(
    IInstitutionSettingsProvider settings,
    ILogger<ClientInstitutionTimeZone> logger) : IInstitutionTimeZone
{
    private string? _resolvidoPara;
    private TimeZoneInfo _fuso = TimeZoneInfo.Local;

    public TimeZoneInfo TimeZone
    {
        get
        {
            var configurado = settings.Current.TimeZoneId;

            if (!string.Equals(configurado, _resolvidoPara, StringComparison.Ordinal))
            {
                _fuso = Resolve(configurado);
                _resolvidoPara = configurado;
            }

            return _fuso;
        }
    }

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    public DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZone);

    private TimeZoneInfo Resolve(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Cai no fuso do aparelho e registra: é melhor do que não abrir, e a tela de
            // diagnóstico mostra qual fuso está realmente em uso.
            logger.LogError(ex, "Fuso {TimeZoneId} não encontrado neste aparelho. Usando o fuso local.", id);
            return TimeZoneInfo.Local;
        }
    }
}
