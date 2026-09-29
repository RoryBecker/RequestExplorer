using System.Text.Json;
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

public sealed class PostmanInfo
{
    [JsonPropertyName("_postman_id")]
    public string PostmanId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Collection";

    [JsonPropertyName("schema")]
    public string Schema { get; set; } = PostmanCollectionMapper.SchemaV21;
}

public sealed class PostmanItem
{
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

public sealed class PostmanUrl
{
    [JsonPropertyName("raw")]
    public string Raw { get; set; } = string.Empty;

    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Protocol { get; set; }

    [JsonPropertyName("host")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Host { get; set; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Path { get; set; }

    [JsonPropertyName("query")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanKeyValue>? Query { get; set; }
}

public sealed class PostmanKeyValue
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("disabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Disabled { get; set; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }
}

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

public sealed class PostmanBodyOptions
{
    [JsonPropertyName("raw")]
    public PostmanRawBodyOptions? Raw { get; set; }
}

public sealed class PostmanRawBodyOptions
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "json";
}
