using ChecklistPlantao.Application.Administration;
using ChecklistPlantao.Application.Checklist;
using ChecklistPlantao.Application.Configuration;
using ChecklistPlantao.Application.Devices;
using ChecklistPlantao.Application.Sessions;
using ChecklistPlantao.Application.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Casos de uso do servidor. Todos são <c>Scoped</c> porque dependem do
    /// <c>IAppDataContext</c>, que é a unidade de trabalho da requisição.
    /// </summary>
    public static IServiceCollection AddChecklistApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<SessionService>();
        services.AddScoped<ChecklistMutationService>();
        services.AddScoped<ConfigurationQueryService>();
        services.AddScoped<SyncService>();
        services.AddScoped<StructureAdminService>();
        services.AddScoped<AccessAdminService>();
        services.AddScoped<SystemAdminService>();
        services.AddScoped<DeviceService>();

        return services;
    }
}
