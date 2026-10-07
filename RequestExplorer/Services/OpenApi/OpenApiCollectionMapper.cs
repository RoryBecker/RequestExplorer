using System.Text.Json;
using System.Text.RegularExpressions;
using RequestExplorer.Models;

namespace RequestExplorer.Services.OpenApi;

/// <summary>
/// Imports OpenAPI 3.x or Swagger 2.0 JSON documents into a <see cref="CollectionDefinition"/>.
/// </summary>
public static class OpenApiCollectionMapper
{
    private static readonly Regex PathParamRegex = new(@"\{([^}/]+)\}", RegexOptions.Compiled);

    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "post", "put", "patch", "delete", "head", "options"
    };

    public static bool LooksLikeOpenApiJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("openapi", out _) || root.TryGetProperty("swagger", out _))
            {
                return root.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Object;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static CollectionDefinition FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("OpenAPI document must be a JSON object.");
        }

        var isOpenApi3 = root.TryGetProperty("openapi", out _);
        var isSwagger2 = root.TryGetProperty("swagger", out var swaggerVer)
                         && swaggerVer.ValueKind == JsonValueKind.String
                         && swaggerVer.GetString()?.StartsWith("2.", StringComparison.Ordinal) == true;

        if (!isOpenApi3 && !isSwagger2)
        {
            throw new InvalidOperationException(
                "Not an OpenAPI/Swagger document. Expected an \"openapi\" or \"swagger\" property.");
        }

        if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("OpenAPI document has no \"paths\" object.");
        }

        var title = ReadInfoTitle(root) ?? (isSwagger2 ? "Swagger Import" : "OpenAPI Import");
        var baseUrl = isOpenApi3 ? ResolveOpenApi3BaseUrl(root) : ResolveSwagger2BaseUrl(root);

        var collection = new CollectionDefinition
        {
            Id = Guid.NewGuid(),
            Name = title
        };

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            collection.Variables.Add(new KeyValueItem
            {
                IsEnabled = true,
                Key = "baseUrl",
                Value = baseUrl.TrimEnd('/')
            });
        }

        var foldersByTag = new Dictionary<string, FolderDefinition>(StringComparer.OrdinalIgnoreCase);
        var untagged = new List<RequestDefinition>();

        foreach (var pathProperty in paths.EnumerateObject())
        {
            var pathTemplate = pathProperty.Name;
            if (pathProperty.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var pathItem = pathProperty.Value;
            var pathLevelParameters = ReadParameters(root, pathItem, isOpenApi3);

            foreach (var methodProperty in pathItem.EnumerateObject())
            {
                if (!HttpMethods.Contains(methodProperty.Name)
                    || methodProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryParseVerb(methodProperty.Name, out var verb))
                {
                    continue;
                }

                var operation = methodProperty.Value;
                var operationParameters = ReadParameters(root, operation, isOpenApi3);
                var mergedParameters = MergeParameters(pathLevelParameters, operationParameters);

                var request = BuildRequest(
                    root,
                    verb,
                    pathTemplate,
                    operation,
                    mergedParameters,
                    isOpenApi3);

                var tag = ReadFirstTag(operation);
                if (string.IsNullOrWhiteSpace(tag))
                {
                    untagged.Add(request);
                }
                else
                {
                    if (!foldersByTag.TryGetValue(tag, out var folder))
                    {
                        folder = new FolderDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = tag
                        };
                        foldersByTag[tag] = folder;
                        collection.Folders.Add(folder);
                    }

                    folder.Requests.Add(request);
                }
            }
        }

        collection.Requests.AddRange(untagged);

        if (collection.Folders.Count == 0
            && collection.Requests.Count == 0)
        {
            throw new InvalidOperationException("OpenAPI document contains no HTTP operations to import.");
        }

        return collection;
    }

    private static RequestDefinition BuildRequest(
        JsonElement root,
        HttpVerb verb,
        string pathTemplate,
        JsonElement operation,
        List<OpenApiParameter> parameters,
        bool isOpenApi3)
    {
        var name = ReadString(operation, "summary")
                   ?? ReadString(operation, "operationId")
                   ?? $"{verb} {pathTemplate}";

        var addressPath = PathParamRegex.Replace(pathTemplate, "{{$1}}");
        if (!addressPath.StartsWith('/'))
        {
            addressPath = "/" + addressPath;
        }

        var address = string.IsNullOrWhiteSpace(addressPath)
            ? "{{baseUrl}}"
            : "{{baseUrl}}" + addressPath;

        var request = new RequestDefinition
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Verb = verb,
            Address = address,
            ExpectedStatusCode = ReadPreferredStatusCode(operation)
        };

        foreach (var parameter in parameters)
        {
            var value = parameter.Example ?? parameter.DefaultValue ?? string.Empty;
            switch (parameter.In.ToLowerInvariant())
            {
                case "query":
                    request.Params.Add(new KeyValueItem
                    {
                        IsEnabled = parameter.Required || !string.IsNullOrEmpty(value),
                        Key = parameter.Name,
                        Value = value
                    });
                    break;
                case "header":
                    if (parameter.Name.Equals("Accept", StringComparison.OrdinalIgnoreCase)
                        || parameter.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
                        || parameter.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    request.Headers.Add(new KeyValueItem
                    {
                        IsEnabled = parameter.Required || !string.IsNullOrEmpty(value),
                        Key = parameter.Name,
                        Value = value
                    });
                    break;
                case "path":
                    // Path tokens already converted to {{name}} in the address.
                    break;
            }
        }

        ApplyRequestBody(root, request, operation, parameters, isOpenApi3);
        return request;
    }

    private static void ApplyRequestBody(
        JsonElement root,
        RequestDefinition request,
        JsonElement operation,
        List<OpenApiParameter> parameters,
        bool isOpenApi3)
    {
        if (isOpenApi3)
        {
            if (!TryGetObjectProperty(operation, "requestBody", out var requestBody))
            {
                return;
            }

            requestBody = ResolveRef(root, requestBody);
            if (!TryGetObjectProperty(requestBody, "content", out var content)
                || content.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (TryPickJsonMedia(content, out var mediaType, out var mediaObject))
            {
                request.Body.ContentType = mediaType;
                request.Body.Content = ExtractExampleOrPlaceholder(mediaObject);
                EnsureContentTypeHeader(request, mediaType);
            }

            return;
        }

        // Swagger 2: body / formData parameters
        var bodyParam = parameters.FirstOrDefault(p =>
            p.In.Equals("body", StringComparison.OrdinalIgnoreCase));
        if (bodyParam is not null)
        {
            request.Body.ContentType = "application/json";
            request.Body.Content = bodyParam.Example
                                   ?? bodyParam.DefaultValue
                                   ?? "{\n  \n}";
            EnsureContentTypeHeader(request, "application/json");
        }
    }

    private static void EnsureContentTypeHeader(RequestDefinition request, string mediaType)
    {
        if (request.Headers.Any(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        request.Headers.Add(new KeyValueItem
        {
            IsEnabled = true,
            Key = "Content-Type",
            Value = mediaType
        });
    }

    private static bool TryPickJsonMedia(JsonElement content, out string mediaType, out JsonElement mediaObject)
    {
        mediaType = "application/json";
        mediaObject = default;

        foreach (var preferred in new[] { "application/json", "application/*+json", "text/json" })
        {
            foreach (var property in content.EnumerateObject())
            {
                if (property.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase)
                    || (preferred.Contains('*')
                        && property.Name.Contains("json", StringComparison.OrdinalIgnoreCase)))
                {
                    mediaType = property.Name;
                    mediaObject = property.Value;
                    return true;
                }
            }
        }

        foreach (var property in content.EnumerateObject())
        {
            mediaType = property.Name;
            mediaObject = property.Value;
            return true;
        }

        return false;
    }

    private static string ExtractExampleOrPlaceholder(JsonElement mediaObject)
    {
        if (mediaObject.ValueKind != JsonValueKind.Object)
        {
            return "{\n  \n}";
        }

        if (mediaObject.TryGetProperty("example", out var example))
        {
            return example.ValueKind == JsonValueKind.String
                ? example.GetString() ?? string.Empty
                : example.GetRawText();
        }

        if (mediaObject.TryGetProperty("examples", out var examples)
            && examples.ValueKind == JsonValueKind.Object)
        {
            foreach (var ex in examples.EnumerateObject())
            {
                if (ex.Value.ValueKind == JsonValueKind.Object
                    && ex.Value.TryGetProperty("value", out var value))
                {
                    return value.ValueKind == JsonValueKind.String
                        ? value.GetString() ?? string.Empty
                        : value.GetRawText();
                }
            }
        }

        return "{\n  \n}";
    }

    private static int ReadPreferredStatusCode(JsonElement operation)
    {
        if (!TryGetObjectProperty(operation, "responses", out var responses)
            || responses.ValueKind != JsonValueKind.Object)
        {
            return 200;
        }

        foreach (var property in responses.EnumerateObject())
        {
            if (property.Name.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(property.Name, out var code) && code is >= 200 and < 300)
            {
                return code;
            }
        }

        foreach (var property in responses.EnumerateObject())
        {
            if (int.TryParse(property.Name, out var code) && code is >= 100 and <= 599)
            {
                return code;
            }
        }

        return 200;
    }

    private static List<OpenApiParameter> ReadParameters(JsonElement root, JsonElement owner, bool isOpenApi3)
    {
        var list = new List<OpenApiParameter>();
        if (!owner.TryGetProperty("parameters", out var parameters)
            || parameters.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in parameters.EnumerateArray())
        {
            var resolved = ResolveRef(root, item);
            if (resolved.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadString(resolved, "name");
            var location = ReadString(resolved, "in");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(location))
            {
                continue;
            }

            var required = resolved.TryGetProperty("required", out var req)
                           && req.ValueKind == JsonValueKind.True;

            string? example = null;
            string? defaultValue = null;

            if (resolved.TryGetProperty("example", out var ex))
            {
                example = JsonValueToString(ex);
            }

            if (isOpenApi3
                && resolved.TryGetProperty("schema", out var schema)
                && schema.ValueKind == JsonValueKind.Object)
            {
                schema = ResolveRef(root, schema);
                if (schema.TryGetProperty("default", out var def))
                {
                    defaultValue = JsonValueToString(def);
                }

                if (example is null && schema.TryGetProperty("example", out var schemaEx))
                {
                    example = JsonValueToString(schemaEx);
                }
            }
            else if (!isOpenApi3)
            {
                if (resolved.TryGetProperty("default", out var def2))
                {
                    defaultValue = JsonValueToString(def2);
                }
            }

            list.Add(new OpenApiParameter(name!, location!, required, example, defaultValue));
        }

        return list;
    }

    private static List<OpenApiParameter> MergeParameters(
        List<OpenApiParameter> pathLevel,
        List<OpenApiParameter> operationLevel)
    {
        var map = new Dictionary<string, OpenApiParameter>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in pathLevel)
        {
            map[$"{parameter.In}:{parameter.Name}"] = parameter;
        }

        foreach (var parameter in operationLevel)
        {
            map[$"{parameter.In}:{parameter.Name}"] = parameter;
        }

        return map.Values.ToList();
    }

    private static JsonElement ResolveRef(JsonElement root, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("$ref", out var refNode)
            || refNode.ValueKind != JsonValueKind.String)
        {
            return element;
        }

        var reference = refNode.GetString();
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return element;
        }

        var current = root;
        foreach (var segment in reference[2..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var decoded = Uri.UnescapeDataString(segment.Replace("~1", "/").Replace("~0", "~"));
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(decoded, out current))
            {
                return element;
            }
        }

        return current;
    }

    private static string? ReadFirstTag(JsonElement operation)
    {
        if (!operation.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var tag in tags.EnumerateArray())
        {
            if (tag.ValueKind == JsonValueKind.String)
            {
                var value = tag.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static string? ReadInfoTitle(JsonElement root)
    {
        if (!TryGetObjectProperty(root, "info", out var info))
        {
            return null;
        }

        return ReadString(info, "title");
    }

    private static string ResolveOpenApi3BaseUrl(JsonElement root)
    {
        if (!root.TryGetProperty("servers", out var servers)
            || servers.ValueKind != JsonValueKind.Array
            || servers.GetArrayLength() == 0)
        {
            return "http://localhost";
        }

        var first = servers[0];
        if (first.ValueKind != JsonValueKind.Object)
        {
            return "http://localhost";
        }

        var url = ReadString(first, "url") ?? "http://localhost";
        if (first.TryGetProperty("variables", out var variables)
            && variables.ValueKind == JsonValueKind.Object)
        {
            foreach (var variable in variables.EnumerateObject())
            {
                var defaultValue = "default";
                if (variable.Value.ValueKind == JsonValueKind.Object
                    && variable.Value.TryGetProperty("default", out var def))
                {
                    defaultValue = JsonValueToString(def) ?? defaultValue;
                }

                url = url.Replace("{" + variable.Name + "}", defaultValue, StringComparison.Ordinal);
            }
        }

        return url;
    }

    private static string ResolveSwagger2BaseUrl(JsonElement root)
    {
        var scheme = "https";
        if (root.TryGetProperty("schemes", out var schemes)
            && schemes.ValueKind == JsonValueKind.Array
            && schemes.GetArrayLength() > 0
            && schemes[0].ValueKind == JsonValueKind.String)
        {
            scheme = schemes[0].GetString() ?? scheme;
        }

        var host = ReadString(root, "host") ?? "localhost";
        var basePath = ReadString(root, "basePath") ?? string.Empty;
        if (!string.IsNullOrEmpty(basePath) && !basePath.StartsWith('/'))
        {
            basePath = "/" + basePath;
        }

        return $"{scheme}://{host}{basePath.TrimEnd('/')}";
    }

    private static bool TryParseVerb(string method, out HttpVerb verb)
    {
        verb = HttpVerb.GET;
        if (!Enum.TryParse(method, ignoreCase: true, out HttpVerb parsed))
        {
            return false;
        }

        verb = parsed;
        return true;
    }

    private static bool TryGetObjectProperty(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        if (!parent.TryGetProperty(name, out value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.Object;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? JsonValueToString(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => value.GetRawText()
        };

    private sealed record OpenApiParameter(
        string Name,
        string In,
        bool Required,
        string? Example,
        string? DefaultValue);
}
