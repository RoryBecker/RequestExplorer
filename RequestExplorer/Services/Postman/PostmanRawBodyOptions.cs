using System.Text.Json.Serialization;

namespace RequestExplorer.Services.Postman;

public sealed class PostmanRawBodyOptions
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "json";
}
