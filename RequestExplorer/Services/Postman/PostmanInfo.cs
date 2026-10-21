using System.Text.Json.Serialization;

namespace RequestExplorer.Services.Postman;

public sealed class PostmanInfo
{
    [JsonPropertyName("_postman_id")]
    public string PostmanId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Collection";

    [JsonPropertyName("schema")]
    public string Schema { get; set; } = PostmanCollectionMapper.SchemaV21;
}
