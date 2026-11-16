using System.Text.Json.Serialization;

namespace RequestExplorer.Services.Postman;

public sealed class PostmanBody
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "raw";

    [JsonPropertyName("raw")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Raw { get; set; }

    [JsonPropertyName("formdata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanFormdataItem>? Formdata { get; set; }

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PostmanBodyOptions? Options { get; set; }
}
