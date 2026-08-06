using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChecklistPlantao.Contracts.Sync;

/// <summary>
/// Corpo de uma operação de marcação. Vai serializado em <see cref="SyncOperationDto.Payload"/>.
/// Os identificadores de sessão, leito, tipo e coluna acompanham a operação para que o servidor
/// possa criar a entrada caso ela ainda não exista — o cliente marca offline em células que o
/// servidor talvez nunca tenha materializado.
/// </summary>
public sealed record ChecklistEntryPayload(
    Guid SessionId,
    Guid BedId,
    Guid ChecklistTemplateId,
    Guid ChecklistColumnId,
    bool IsCompleted);

public sealed record SessionBedMarkerPayload(
    Guid SessionId,
    Guid BedId,
    Guid MarkerDefinitionId,
    bool IsSelected);

/// <summary>
/// Serialização compartilhada entre servidor e cliente. Uma única configuração evita que os dois
/// lados divirjam em maiúsculas de propriedade ou formato de data.
/// </summary>
public static class SyncJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
