using System.Text.Json.Serialization;

namespace RequestExplorer.Services.Postman;

public sealed class PostmanAuth
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "noauth";

    [JsonPropertyName("bearer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanKeyValue>? Bearer { get; set; }

    [JsonPropertyName("basic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanKeyValue>? Basic { get; set; }

    [JsonPropertyName("apikey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanKeyValue>? Apikey { get; set; }
}
