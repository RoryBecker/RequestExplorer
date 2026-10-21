using System.Text.Json;
using System.Text.Json.Serialization;

namespace RequestExplorer.Services.Postman;

public sealed class PostmanItem
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Item";

    /// <summary>Present for folders.</summary>
    [JsonPropertyName("item")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanItem>? Item { get; set; }

    [JsonPropertyName("variable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanKeyValue>? Variable { get; set; }

    /// <summary>Present for requests. May be a string URL or a request object.</summary>
    [JsonPropertyName("request")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Request { get; set; }
}
