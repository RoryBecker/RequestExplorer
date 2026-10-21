using System.Text.Json.Serialization;

namespace RequestExplorer.Services.Postman;

public sealed class PostmanBodyOptions
{
    [JsonPropertyName("raw")]
    public PostmanRawBodyOptions? Raw { get; set; }
}
