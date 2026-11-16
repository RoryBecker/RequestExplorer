using System.Text.Json;
using System.Text.Json.Serialization;

namespace RequestExplorer.Services.Postman;

public sealed class PostmanFormdataItem
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>
    /// Postman file path(s). We round-trip our base64 via <see cref="Value"/> and keep the
    /// file name here when exporting.
    /// </summary>
    [JsonPropertyName("src")]
    public JsonElement? Src { get; set; }

    [JsonPropertyName("disabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Disabled { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}
