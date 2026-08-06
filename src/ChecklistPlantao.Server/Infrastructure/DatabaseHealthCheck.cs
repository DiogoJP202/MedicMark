using ChecklistPlantao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ChecklistPlantao.Server.Infrastructure;

/// <summary>
/// Verifica se o banco central responde e se não há migration pendente.
///
/// Escrito à mão em vez de usar o pacote <c>HealthChecks.EntityFrameworkCore</c>: são vinte
/// linhas e evita mais uma dependência para manter atualizada.
/// </summary>
public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return HealthCheckResult.Unhealthy("O banco de dados não respondeu.");
            }

            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
            var pendingList = pending.ToList();

            return pendingList.Count > 0
                ? HealthCheckResult.Degraded($"Há {pendingList.Count} migration(s) pendente(s).")
                : HealthCheckResult.Healthy("Banco disponível e atualizado.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Falha ao consultar o banco de dados.", ex);
        }
    }
}
