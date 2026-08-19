using System.ComponentModel.DataAnnotations;
using ChecklistPlantao.Application.Sessions;
using ChecklistPlantao.Infrastructure.Persistence;
using ChecklistPlantao.Server.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChecklistPlantao.Server.Infrastructure;

public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    /// <summary>Intervalo entre as passagens de manutenção.</summary>
    [Range(1, 1440, ErrorMessage = "Maintenance:IntervalMinutes precisa estar entre 1 e 1440.")]
    public int IntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Dias que o log de alterações é mantido antes de ser podado. Zero poda tudo o que já foi
    /// consumido; negativo não tem significado e é recusado na subida.
    /// </summary>
    [Range(0, 3650, ErrorMessage = "Maintenance:ChangeLogRetentionDays precisa estar entre 0 e 3650.")]
    public int ChangeLogRetentionDays { get; set; } = 30;

    /// <summary>Dias que o registro de operações idempotentes é mantido.</summary>
    [Range(0, 3650, ErrorMessage = "Maintenance:ProcessedOperationRetentionDays precisa estar entre 0 e 3650.")]
    public int ProcessedOperationRetentionDays { get; set; } = 7;

    /// <summary>Dias sem contato até um dispositivo ser marcado como inativo.</summary>
    [Range(1, 3650, ErrorMessage = "Maintenance:DeviceInactivityDays precisa estar entre 1 e 3650.")]
    public int DeviceInactivityDays { get; set; } = 30;
}

/// <summary>
/// Manutenção periódica: retenção das sessões, poda do log de sincronização, limpeza de tokens
/// expirados e marcação de dispositivos sem contato.
///
/// Roda em intervalo largo e sempre em um escopo próprio. Não é um laço apertado: o objetivo é
/// higiene do banco, não reação imediata.
/// </summary>
public sealed class MaintenanceHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<MaintenanceOptions> options,
    ILogger<MaintenanceHostedService> logger) : BackgroundService
{
    private readonly MaintenanceOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Espera inicial para não competir com migrations e seed durante a subida.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes)));

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);

            do
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal do host.
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
            var devices = scope.ServiceProvider.GetRequiredService<Application.Devices.DeviceService>();
            var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var purgedSessions = await sessions.PurgeExpiredAsync(cancellationToken).ConfigureAwait(false);
            var purgedTokens = await tokens.PurgeExpiredAsync(cancellationToken).ConfigureAwait(false);

            var changeLogLimit = DateTime.UtcNow.AddDays(-_options.ChangeLogRetentionDays);
            var purgedChanges = await db.ChangeLog
                .Where(c => c.ChangedAtUtc < changeLogLimit)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var operationLimit = DateTime.UtcNow.AddDays(-_options.ProcessedOperationRetentionDays);
            var purgedOperations = await db.ProcessedOperations
                .Where(p => p.ProcessedAtUtc < operationLimit)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var staleDevices = await devices
                .DeactivateStaleAsync(TimeSpan.FromDays(_options.DeviceInactivityDays), cancellationToken)
                .ConfigureAwait(false);

            if (purgedSessions + purgedTokens + purgedChanges + purgedOperations + staleDevices > 0)
            {
                logger.LogInformation(
                    "Manutenção: {Sessoes} sessão(ões), {Tokens} token(s), {Alteracoes} alteração(ões), {Operacoes} operação(ões), {Dispositivos} dispositivo(s).",
                    purgedSessions,
                    purgedTokens,
                    purgedChanges,
                    purgedOperations,
                    staleDevices);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Uma falha de manutenção nunca pode derrubar o servidor: o plantão continua.
            logger.LogError(ex, "Falha durante a manutenção periódica. Nova tentativa no próximo ciclo.");
        }
    }
}
