using System.Text.Json.Serialization;

namespace RequestExplorer.Services.Postman;

public sealed class PostmanBody
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "raw";

    [JsonPropertyName("raw")]
    public string Raw { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PostmanBodyOptions? Options { get; set; }
}
