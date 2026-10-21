using System.Text.Json.Serialization;

namespace RequestExplorer.Services.Postman;

public sealed class PostmanCollectionDocument
{
    [JsonPropertyName("info")]
    public PostmanInfo Info { get; set; } = new();

    [JsonPropertyName("variable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanKeyValue>? Variable { get; set; }

    [JsonPropertyName("item")]
    public List<PostmanItem> Item { get; set; } = [];
}
