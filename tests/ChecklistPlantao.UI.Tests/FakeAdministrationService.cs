using ChecklistPlantao.Contracts.Administration;
using ChecklistPlantao.Contracts.Configuration;
using ChecklistPlantao.Contracts.Devices;
using ChecklistPlantao.UI.Abstractions;

namespace ChecklistPlantao.UI.Tests;

/// <summary>
/// Administração de mentira: devolve o que o teste montar e GUARDA o que a tela enviou.
///
/// Guardar a última requisição é o essencial aqui — vários defeitos destas telas não estão no
/// que aparece, e sim no que é enviado ao servidor. Foi assim que "editar tipo" apagava os
/// setores: a tela mandava lista vazia, e nada na aparência denunciava.
/// </summary>
internal sealed class FakeAdministrationService : IAdministrationService
{
    public List<SectorDto> Sectors { get; } = [];

    public List<BedDto> Beds { get; } = [];

    public List<ChecklistTemplateDto> Templates { get; } = [];

    public List<BedMarkerDefinitionDto> Markers { get; } = [];

    public List<DeviceDto> Devices { get; } = [];

    /// <summary>Resposta das operações de gravação. O teste troca por falha quando quiser.</summary>
    public Result SaveResult { get; set; } = Result.Ok();

    public SaveSectorRequest? LastSector { get; private set; }

    public SaveBedRequest? LastBed { get; private set; }

    public SaveTemplateRequest? LastTemplate { get; private set; }

    public SaveColumnRequest? LastColumn { get; private set; }

    public SaveMarkerRequest? LastMarker { get; private set; }

    public Guid? LastEditedId { get; private set; }

    public int SaveCount { get; private set; }

    public Task<IReadOnlyList<SectorDto>> GetSectorsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SectorDto>>(Sectors);

    public Task<Result> SaveSectorAsync(Guid? id, SaveSectorRequest request, CancellationToken cancellationToken = default)
    {
        LastSector = request;
        return Registrar(id);
    }

    public Task<IReadOnlyList<BedDto>> GetBedsAsync(Guid? sectorId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BedDto>>(Beds);

    public Task<Result> SaveBedAsync(Guid? id, SaveBedRequest request, CancellationToken cancellationToken = default)
    {
        LastBed = request;
        return Registrar(id);
    }

    public Task<IReadOnlyList<ChecklistTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChecklistTemplateDto>>(Templates);

    public Task<Result> SaveTemplateAsync(Guid? id, SaveTemplateRequest request, CancellationToken cancellationToken = default)
    {
        LastTemplate = request;
        return Registrar(id);
    }

    public Task<Result> SaveColumnAsync(Guid templateId, Guid? columnId, SaveColumnRequest request, CancellationToken cancellationToken = default)
    {
        LastColumn = request;
        return Registrar(columnId);
    }

    public Task<IReadOnlyList<BedMarkerDefinitionDto>> GetMarkersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BedMarkerDefinitionDto>>(Markers);

    public Task<Result> SaveMarkerAsync(Guid? id, SaveMarkerRequest request, CancellationToken cancellationToken = default)
    {
        LastMarker = request;
        return Registrar(id);
    }

    public Task<IReadOnlyList<AccessGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AccessGroupDto>>([]);

    public Task<Result> SaveGroupAsync(Guid? id, SaveGroupRequest request, CancellationToken cancellationToken = default) =>
        Registrar(id);

    public Task<IReadOnlyList<AppUserDto>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AppUserDto>>([]);

    public Task<Result> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default) =>
        Registrar(null);

    public Task<Result> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default) =>
        Registrar(id);

    public Task<Result> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken = default) =>
        Registrar(id);

    public Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PermissionDto>>([]);

    public Task<NotificationConfigurationDto> GetNotificationConfigurationAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new NotificationConfigurationDto(true, true, "High", true, true, false, "{checklist}", "{pendentes}", 1));

    public Task<Result> SaveNotificationConfigurationAsync(SaveNotificationConfigurationRequest request, CancellationToken cancellationToken = default) =>
        Registrar(null);

    public Task<InstitutionSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new InstitutionSettingsDto("America/Sao_Paulo", new TimeOnly(19, 0), new TimeOnly(7, 0), 24, 7, 5, true));

    public Task<Result> SaveSettingsAsync(SaveInstitutionSettingsRequest request, CancellationToken cancellationToken = default) =>
        Registrar(null);

    public Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DeviceDto>>(Devices);

    private Task<Result> Registrar(Guid? id)
    {
        LastEditedId = id;
        SaveCount++;
        return Task.FromResult(SaveResult);
    }
}
