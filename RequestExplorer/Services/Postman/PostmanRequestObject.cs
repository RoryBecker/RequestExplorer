using System.Text.Json.Serialization;

namespace RequestExplorer.Services.Postman;

public sealed class PostmanRequestObject
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = "GET";

    [JsonPropertyName("header")]
    public List<PostmanKeyValue> Header { get; set; } = [];

    [JsonPropertyName("url")]
    public PostmanUrl Url { get; set; } = new();

    [JsonPropertyName("auth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PostmanAuth? Auth { get; set; }

    [JsonPropertyName("body")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PostmanBody? Body { get; set; }
}
