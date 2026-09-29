using System.Text.Json;
using System.Text.Json.Serialization;
using RequestExplorer.Models;

namespace RequestExplorer.Services.Postman;

public static class PostmanCollectionMapper
{
    public const string SchemaV21 = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static PostmanCollectionDocument ToCollectionDocument(CollectionDefinition collection) =>
        new()
        {
            Info = new PostmanInfo
            {
                PostmanId = collection.Id.ToString(),
                Name = collection.Name
            },
            Item = ToItems(collection.Folders, collection.Requests)
        };

    public static PostmanCollectionDocument ToCollectionDocument(FolderDefinition folder) =>
        new()
        {
            Info = new PostmanInfo
            {
                PostmanId = folder.Id.ToString(),
                Name = folder.Name
            },
            Item = ToItems(folder.Folders, folder.Requests)
        };

    public static PostmanCollectionDocument ToCollectionDocument(RequestDefinition request) =>
        new()
        {
            Info = new PostmanInfo
            {
                PostmanId = request.Id.ToString(),
                Name = request.Name
            },
            Item = [ToRequestItem(request)]
        };

    public static CollectionDefinition FromCollectionDocument(PostmanCollectionDocument document)
    {
        var collection = new CollectionDefinition
        {
            Name = string.IsNullOrWhiteSpace(document.Info.Name) ? "Imported Collection" : document.Info.Name
        };

        if (Guid.TryParse(document.Info.PostmanId, out var id))
        {
            collection.Id = id;
        }

        ApplyItems(document.Item, collection.Folders, collection.Requests);
        return collection;
    }

    public static void ApplyItems(
        IEnumerable<PostmanItem> items,
        List<FolderDefinition> folders,
        List<RequestDefinition> requests)
    {
        foreach (var item in items)
        {
            if (item.Item is not null)
            {
                var folder = new FolderDefinition { Name = item.Name };
                ApplyItems(item.Item, folder.Folders, folder.Requests);
                folders.Add(folder);
            }
            else if (item.Request is not null)
            {
                requests.Add(FromRequestItem(item));
            }
        }
    }

    private static List<PostmanItem> ToItems(IEnumerable<FolderDefinition> folders, IEnumerable<RequestDefinition> requests)
    {
        var items = new List<PostmanItem>();
        foreach (var folder in folders)
        {
            items.Add(new PostmanItem
            {
                Name = folder.Name,
                Item = ToItems(folder.Folders, folder.Requests)
            });
        }

        foreach (var request in requests)
        {
            items.Add(ToRequestItem(request));
        }

        return items;
    }

    private static PostmanItem ToRequestItem(RequestDefinition request)
    {
        var requestObject = new PostmanRequestObject
        {
            Method = request.Verb.ToString().ToUpperInvariant(),
            Header = request.Headers
                .Where(h => !string.IsNullOrWhiteSpace(h.Key))
                .Select(h => new PostmanKeyValue
                {
                    Key = h.Key,
                    Value = h.Value,
                    Disabled = h.IsEnabled ? null : true
                })
                .ToList(),
            Url = BuildUrl(request),
            Auth = BuildAuth(request.Authentication),
            Body = BuildBody(request)
        };

        return new PostmanItem
        {
            Name = request.Name,
            Request = JsonSerializer.SerializeToElement(requestObject, JsonOptions)
        };
    }

    private static RequestDefinition FromRequestItem(PostmanItem item)
    {
        var request = new RequestDefinition
        {
            Name = item.Name
        };

        if (item.Request is null)
        {
            return request;
        }

        var element = item.Request.Value;
        if (element.ValueKind == JsonValueKind.String)
        {
            request.Address = element.GetString() ?? string.Empty;
            request.Verb = HttpVerb.GET;
            return request;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return request;
        }

        var obj = JsonSerializer.Deserialize<PostmanRequestObject>(element.GetRawText(), JsonOptions)
                  ?? new PostmanRequestObject();

        if (Enum.TryParse<HttpVerb>(obj.Method, ignoreCase: true, out var verb))
        {
            request.Verb = verb;
        }

        request.Address = obj.Url?.Raw ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.Address) && obj.Url is not null)
        {
            request.Address = ReconstructRawUrl(obj.Url);
        }

        foreach (var header in obj.Header)
        {
            if (string.IsNullOrWhiteSpace(header.Key))
            {
                continue;
            }

            request.Headers.Add(new KeyValueItem
            {
                Key = header.Key ?? string.Empty,
                Value = header.Value ?? string.Empty,
                IsEnabled = header.Disabled != true
            });
        }

        if (obj.Url?.Query is { Count: > 0 })
        {
            foreach (var query in obj.Url.Query)
            {
                if (string.IsNullOrWhiteSpace(query.Key))
                {
                    continue;
                }

                request.Params.Add(new KeyValueItem
                {
                    Key = query.Key ?? string.Empty,
                    Value = query.Value ?? string.Empty,
                    IsEnabled = query.Disabled != true
                });
            }

            // Keep Address base without query when Params are populated; RequestNodeViewModel will sync.
            if (!string.IsNullOrWhiteSpace(request.Address))
            {
                var q = request.Address.IndexOf('?', StringComparison.Ordinal);
                if (q >= 0)
                {
                    request.Address = request.Address[..q];
                }
            }
        }

        ApplyAuth(request, obj.Auth);

        if (obj.Body is { Mode: "raw" })
        {
            request.Body.Content = obj.Body.Raw ?? string.Empty;
            var language = obj.Body.Options?.Raw?.Language;
            request.Body.ContentType = language?.Equals("json", StringComparison.OrdinalIgnoreCase) == true
                ? "application/json"
                : request.Body.ContentType;
        }

        return request;
    }

    private static PostmanUrl BuildUrl(RequestDefinition request)
    {
        var enabledParams = request.Params
            .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key))
            .ToList();

        var raw = request.Address?.Trim() ?? string.Empty;
        if (enabledParams.Count > 0)
        {
            var query = string.Join("&", enabledParams.Select(p =>
                $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value ?? string.Empty)}"));
            var qIndex = raw.IndexOf('?', StringComparison.Ordinal);
            raw = qIndex >= 0 ? $"{raw[..qIndex]}?{query}" : $"{raw}?{query}";
        }

        var url = new PostmanUrl { Raw = raw };
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            if (enabledParams.Count > 0)
            {
                url.Query = enabledParams.Select(p => new PostmanKeyValue
                {
                    Key = p.Key,
                    Value = p.Value,
                    Disabled = null
                }).ToList();
            }

            return url;
        }

        url.Protocol = uri.Scheme;
        url.Host = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
        url.Path = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (enabledParams.Count > 0)
        {
            url.Query = enabledParams.Select(p => new PostmanKeyValue
            {
                Key = p.Key,
                Value = p.Value
            }).ToList();
        }
        else if (!string.IsNullOrEmpty(uri.Query))
        {
            url.Query = ParseQuery(uri.Query);
        }

        return url;
    }

    private static List<PostmanKeyValue> ParseQuery(string query)
    {
        var list = new List<PostmanKeyValue>();
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
            {
                list.Add(new PostmanKeyValue
                {
                    Key = Uri.UnescapeDataString(part.Replace('+', ' ')),
                    Value = string.Empty
                });
            }
            else
            {
                list.Add(new PostmanKeyValue
                {
                    Key = Uri.UnescapeDataString(part[..eq].Replace('+', ' ')),
                    Value = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '))
                });
            }
        }

        return list;
    }

    private static string ReconstructRawUrl(PostmanUrl url)
    {
        if (!string.IsNullOrWhiteSpace(url.Raw))
        {
            return url.Raw;
        }

        var protocol = string.IsNullOrWhiteSpace(url.Protocol) ? "https" : url.Protocol;
        var host = url.Host is { Count: > 0 } ? string.Join('.', url.Host) : string.Empty;
        var path = url.Path is { Count: > 0 } ? "/" + string.Join('/', url.Path) : string.Empty;
        var query = url.Query is { Count: > 0 }
            ? "?" + string.Join("&", url.Query
                .Where(q => !string.IsNullOrWhiteSpace(q.Key) && q.Disabled != true)
                .Select(q => $"{Uri.EscapeDataString(q.Key!)}={Uri.EscapeDataString(q.Value ?? string.Empty)}"))
            : string.Empty;
        return string.IsNullOrEmpty(host) ? string.Empty : $"{protocol}://{host}{path}{query}";
    }

    private static PostmanAuth? BuildAuth(AuthenticationSettings auth)
    {
        return auth.Type switch
        {
            AuthType.None => null,
            AuthType.Bearer => new PostmanAuth
            {
                Type = "bearer",
                Bearer =
                [
                    new PostmanKeyValue { Key = "token", Value = auth.BearerToken, Type = "string" }
                ]
            },
            AuthType.Basic => new PostmanAuth
            {
                Type = "basic",
                Basic =
                [
                    new PostmanKeyValue { Key = "username", Value = auth.BasicUsername, Type = "string" },
                    new PostmanKeyValue { Key = "password", Value = auth.BasicPassword, Type = "string" }
                ]
            },
            AuthType.ApiKey => new PostmanAuth
            {
                Type = "apikey",
                Apikey =
                [
                    new PostmanKeyValue { Key = "key", Value = auth.ApiKeyName, Type = "string" },
                    new PostmanKeyValue { Key = "value", Value = auth.ApiKeyValue, Type = "string" },
                    new PostmanKeyValue
                    {
                        Key = "in",
                        Value = auth.ApiKeyLocation == ApiKeyLocation.Query ? "query" : "header",
                        Type = "string"
                    }
                ]
            },
            _ => null
        };
    }

    private static void ApplyAuth(RequestDefinition request, PostmanAuth? auth)
    {
        if (auth is null || string.Equals(auth.Type, "noauth", StringComparison.OrdinalIgnoreCase))
        {
            request.Authentication.Type = AuthType.None;
            return;
        }

        switch (auth.Type.ToLowerInvariant())
        {
            case "bearer":
                request.Authentication.Type = AuthType.Bearer;
                request.Authentication.BearerToken = FindAuthValue(auth.Bearer, "token") ?? string.Empty;
                break;
            case "basic":
                request.Authentication.Type = AuthType.Basic;
                request.Authentication.BasicUsername = FindAuthValue(auth.Basic, "username") ?? string.Empty;
                request.Authentication.BasicPassword = FindAuthValue(auth.Basic, "password") ?? string.Empty;
                break;
            case "apikey":
                request.Authentication.Type = AuthType.ApiKey;
                request.Authentication.ApiKeyName = FindAuthValue(auth.Apikey, "key") ?? "X-Api-Key";
                request.Authentication.ApiKeyValue = FindAuthValue(auth.Apikey, "value") ?? string.Empty;
                var location = FindAuthValue(auth.Apikey, "in");
                request.Authentication.ApiKeyLocation =
                    string.Equals(location, "query", StringComparison.OrdinalIgnoreCase)
                        ? ApiKeyLocation.Query
                        : ApiKeyLocation.Header;
                break;
            default:
                request.Authentication.Type = AuthType.None;
                break;
        }
    }

    private static string? FindAuthValue(List<PostmanKeyValue>? values, string key) =>
        values?.FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static PostmanBody? BuildBody(RequestDefinition request)
    {
        if (string.IsNullOrEmpty(request.Body.Content)
            && request.Verb is HttpVerb.GET or HttpVerb.HEAD or HttpVerb.OPTIONS or HttpVerb.DELETE)
        {
            return null;
        }

        if (string.IsNullOrEmpty(request.Body.Content))
        {
            return null;
        }

        var isJson = request.Body.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
        return new PostmanBody
        {
            Mode = "raw",
            Raw = request.Body.Content,
            Options = new PostmanBodyOptions
            {
                Raw = new PostmanRawBodyOptions
                {
                    Language = isJson ? "json" : "text"
                }
            }
        };
    }
}
