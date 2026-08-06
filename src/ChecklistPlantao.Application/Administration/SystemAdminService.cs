using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Application.Common;
using ChecklistPlantao.Application.Configuration;
using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Domain.Common;
using ChecklistPlantao.Domain.Notifications;
using ChecklistPlantao.Domain.Settings;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Application.Administration;

/// <summary>Configurações gerais e de notificação.</summary>
public sealed class SystemAdminService(IAppDataContext db, IClock clock, IInstitutionSettingsProvider settingsProvider)
{
    public async Task<NotificationConfigurationDto> GetNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await LoadNotificationConfigurationAsync(cancellationToken).ConfigureAwait(false);
        return ConfigurationQueryService.Map(configuration);
    }

    public async Task<Result<NotificationConfigurationDto>> SaveNotificationsAsync(
        SaveNotificationConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configuration = await LoadNotificationConfigurationAsync(cancellationToken).ConfigureAwait(false);

        if (configuration.Version != request.BaseVersion)
        {
            return OperationError.Conflict("Outra pessoa alterou as notificações enquanto você editava. Atualize a tela e refaça a alteração.");
        }

        if (!Enum.TryParse<NotificationPriority>(request.Priority, ignoreCase: true, out var priority))
        {
            return OperationError.Validation($"Prioridade \"{request.Priority}\" desconhecida.");
        }

        var now = clock.UtcNow;

        try
        {
            configuration.Update(
                request.SoundEnabled,
                request.VibrationEnabled,
                priority,
                request.EnabledOnAndroid,
                request.EnabledOnWindows,
                request.AllowFullScreenIntent,
                request.TitleTemplate,
                request.BodyTemplate,
                now);
        }
        catch (DomainRuleException ex)
        {
            return OperationError.Validation(ex.Message);
        }

        db.AppendChange(SyncEntityTypes.NotificationConfiguration, configuration.Id, SyncChangeType.Updated, configuration.Version, null, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ConfigurationQueryService.Map(configuration);
    }

    public async Task<InstitutionSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider.GetAsync(cancellationToken).ConfigureAwait(false);
        return ConfigurationQueryService.Map(settings);
    }

    /// <summary>
    /// Grava as configurações gerais. Alterar fuso ou janela de turno muda o agendamento de todos
    /// os dispositivos, por isso cada chave alterada entra no log e força um reagendamento.
    /// </summary>
    public async Task<Result<InstitutionSettingsDto>> SaveSettingsAsync(
        SaveInstitutionSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        InstitutionSettings settings;

        try
        {
            settings = new InstitutionSettings
            {
                TimeZoneId = request.TimeZoneId,
                ShiftStart = request.ShiftStart,
                ShiftEnd = request.ShiftEnd,
                RetentionAfterClose = TimeSpan.FromHours(request.RetentionAfterCloseHours),
                OfflineLoginValidity = TimeSpan.FromDays(request.OfflineLoginValidityDays),
                OfflineLoginMaxAttempts = request.OfflineLoginMaxAttempts,
                AutoOpenSession = request.AutoOpenSession,
            }.Validated();
        }
        catch (DomainRuleException ex)
        {
            return OperationError.Validation(ex.Message);
        }

        var now = clock.UtcNow;
        var stored = await db.AppSettings.ToDictionaryAsync(s => s.Key, s => s, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);

        foreach (var (key, value) in InstitutionSettingsSerializer.Flatten(settings))
        {
            if (stored.TryGetValue(key, out var setting))
            {
                if (setting.SetValue(value, now))
                {
                    db.AppendChange(SyncEntityTypes.AppSetting, setting.Id, SyncChangeType.Updated, setting.Version, null, now);
                }
            }
            else
            {
                var created = new AppSetting(key, value, now);
                db.AppSettings.Add(created);
                db.AppendChange(SyncEntityTypes.AppSetting, created.Id, SyncChangeType.Created, created.Version, null, now);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await settingsProvider.ReloadAsync(cancellationToken).ConfigureAwait(false);

        return ConfigurationQueryService.Map(settings);
    }

    private async Task<NotificationConfiguration> LoadNotificationConfigurationAsync(CancellationToken cancellationToken)
    {
        var configuration = await db.NotificationConfigurations
            .FirstOrDefaultAsync(c => c.Id == NotificationConfiguration.SingletonId, cancellationToken)
            .ConfigureAwait(false);

        if (configuration is not null)
        {
            return configuration;
        }

        configuration = new NotificationConfiguration(clock.UtcNow);
        db.NotificationConfigurations.Add(configuration);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return configuration;
    }
}
