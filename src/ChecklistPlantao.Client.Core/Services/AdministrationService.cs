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

/// <summary>
/// Administração pela API. Exige servidor: cadastro não é operação offline — mudar um horário
/// sem o servidor criaria divergência entre aparelhos.
/// </summary>
public sealed class AdministrationService(IServerApi api) : IStructureAdminService, IAccessAdminService, ISystemAdminService
{
    private HttpServerApi Http => api as HttpServerApi
        ?? throw new InvalidOperationException("A administração exige o cliente HTTP do servidor.");

    public async Task<IReadOnlyList<SectorDto>> GetSectorsAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<SectorDto>("api/admin/sectors", cancellationToken).ConfigureAwait(false);

    public Task<UiResult> SaveSectorAsync(Guid? id, SaveSectorRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/admin/sectors" : $"api/admin/sectors/{id}", request, cancellationToken);

    public async Task<IReadOnlyList<BedDto>> GetBedsAsync(Guid? sectorId, CancellationToken cancellationToken = default) =>
        await GetListAsync<BedDto>(sectorId is null ? "api/admin/beds" : $"api/admin/beds?sectorId={sectorId}", cancellationToken).ConfigureAwait(false);

    public Task<UiResult> SaveBedAsync(Guid? id, SaveBedRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/admin/beds" : $"api/admin/beds/{id}", request, cancellationToken);

    public async Task<IReadOnlyList<ChecklistTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<ChecklistTemplateDto>("api/admin/templates", cancellationToken).ConfigureAwait(false);

    public Task<UiResult> SaveTemplateAsync(Guid? id, SaveTemplateRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/admin/templates" : $"api/admin/templates/{id}", request, cancellationToken);

    public Task<UiResult> SaveColumnAsync(Guid templateId, Guid? columnId, SaveColumnRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(
            columnId is null ? HttpMethod.Post : HttpMethod.Put,
            columnId is null ? $"api/admin/templates/{templateId}/columns" : $"api/admin/templates/{templateId}/columns/{columnId}",
            request,
            cancellationToken);

    public async Task<IReadOnlyList<BedMarkerDefinitionDto>> GetMarkersAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<BedMarkerDefinitionDto>("api/admin/markers", cancellationToken).ConfigureAwait(false);

    public Task<UiResult> SaveMarkerAsync(Guid? id, SaveMarkerRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/admin/markers" : $"api/admin/markers/{id}", request, cancellationToken);

    public async Task<IReadOnlyList<AccessGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<AccessGroupDto>("api/admin/groups", cancellationToken).ConfigureAwait(false);

    public Task<UiResult> SaveGroupAsync(Guid? id, SaveGroupRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/admin/groups" : $"api/admin/groups/{id}", request, cancellationToken);

    public async Task<IReadOnlyList<AppUserDto>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<AppUserDto>("api/admin/users", cancellationToken).ConfigureAwait(false);

    public Task<UiResult> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/admin/users", request, cancellationToken);

    public Task<UiResult> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"api/admin/users/{id}", request, cancellationToken);

    public Task<UiResult> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"api/admin/users/{id}/password", request, cancellationToken);

    public async Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<PermissionDto>("api/admin/permissions", cancellationToken).ConfigureAwait(false);

    public async Task<NotificationConfigurationDto> GetNotificationConfigurationAsync(CancellationToken cancellationToken = default) =>
        await Http.SendAsync<NotificationConfigurationDto>(HttpMethod.Get, "api/admin/notifications", null, true, cancellationToken).ConfigureAwait(false)
            ?? new NotificationConfigurationDto(true, true, "High", true, true, false, string.Empty, string.Empty, 0);

    public Task<UiResult> SaveNotificationConfigurationAsync(SaveNotificationConfigurationRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, "api/admin/notifications", request, cancellationToken);

    public async Task<InstitutionSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        await Http.SendAsync<InstitutionSettingsDto>(HttpMethod.Get, "api/admin/settings", null, true, cancellationToken).ConfigureAwait(false)
            ?? new InstitutionSettingsDto("America/Sao_Paulo", new TimeOnly(19, 0), new TimeOnly(7, 0), 24, 7, 5, true);

    public Task<UiResult> SaveSettingsAsync(SaveInstitutionSettingsRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, "api/admin/settings", request, cancellationToken);

    public async Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        await GetListAsync<DeviceDto>("api/admin/devices", cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken) =>
        await Http.SendAsync<List<T>>(HttpMethod.Get, path, null, authenticated: true, cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Traduz a resposta HTTP em um resultado que a tela entende, inclusive o conflito de versão.</summary>
    private async Task<UiResult> SendAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        var resposta = await Http.SendRawAsync(method, path, body, authenticated: true, cancellationToken).ConfigureAwait(false);

        if (resposta is null)
        {
            return UiResult.Fail("O servidor não está acessível. Esta alteração exige conexão.");
        }

        if (resposta.IsSuccessStatusCode)
        {
            return UiResult.Ok();
        }

        try
        {
            var problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);

            var mensagem = problema.TryGetProperty("detail", out var detalhe) ? detalhe.GetString() : null;
            var codigo = problema.TryGetProperty("codigo", out var chave) ? chave.GetString() : null;

            return UiResult.Fail(mensagem ?? "Não foi possível salvar.", codigo);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return UiResult.Fail($"Não foi possível salvar ({(int)resposta.StatusCode}).");
        }
    }
}
