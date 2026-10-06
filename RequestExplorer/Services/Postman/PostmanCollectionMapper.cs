using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RequestExplorer.Models;
using RequestExplorer.ViewModels;

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
            Variable = ToPostmanVariables(collection.Variables),
            Item = ToItems(collection.Folders, collection.Requests, collection.ChildOrder)
        };

    public static PostmanCollectionDocument ToCollectionDocument(FolderDefinition folder) =>
        new()
        {
            Info = new PostmanInfo
            {
                PostmanId = folder.Id.ToString(),
                Name = folder.Name
            },
            Variable = ToPostmanVariables(folder.Variables),
            Item = ToItems(folder.Folders, folder.Requests, folder.ChildOrder)
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

    /// <summary>
    /// Builds a Postman collection with variables inlined into every request and omitted from
    /// collection/folder scopes. Does not mutate the live tree — resolution uses clones.
    /// </summary>
    public static PostmanCollectionDocument ToFlattenedCollectionDocument(TreeNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node switch
        {
            CollectionNodeViewModel collection => new PostmanCollectionDocument
            {
                Info = new PostmanInfo
                {
                    PostmanId = collection.Model.Id.ToString(),
                    Name = collection.Name
                },
                Item = ToFlattenedItems(collection.Children)
            },
            FolderNodeViewModel folder => new PostmanCollectionDocument
            {
                Info = new PostmanInfo
                {
                    PostmanId = folder.Model.Id.ToString(),
                    Name = folder.Name
                },
                Item = ToFlattenedItems(folder.Children)
            },
            RequestNodeViewModel request => new PostmanCollectionDocument
            {
                Info = new PostmanInfo
                {
                    PostmanId = request.Model.Id.ToString(),
                    Name = request.Name
                },
                Item = [ToRequestItem(VariableResolver.Resolve(request.Model, request))]
            },
            _ => throw new InvalidOperationException("Unsupported node type.")
        };
    }

    private static List<PostmanItem> ToFlattenedItems(IEnumerable<TreeNodeViewModel> children)
    {
        var items = new List<PostmanItem>();
        foreach (var child in children)
        {
            switch (child)
            {
                case FolderNodeViewModel folder:
                    items.Add(new PostmanItem
                    {
                        Id = folder.Model.Id.ToString(),
                        Name = folder.Name,
                        Item = ToFlattenedItems(folder.Children)
                    });
                    break;
                case RequestNodeViewModel request:
                    items.Add(ToRequestItem(VariableResolver.Resolve(request.Model, request)));
                    break;
            }
        }

        return items;
    }

    public static CollectionDefinition FromCollectionDocument(PostmanCollectionDocument document)
    {
        var collection = new CollectionDefinition
        {
            Name = ResolveCollectionName(document)
        };

        if (Guid.TryParse(document.Info.PostmanId, out var id))
        {
            collection.Id = id;
        }

        ApplyItems(document.Item, collection.Folders, collection.Requests, collection.ChildOrder);
        ApplyVariables(document.Variable, collection.Variables);
        return collection;
    }

    /// <summary>
    /// Imports a Postman collection as one folder under the import target.
    /// Naming: if the export has a single top-level folder (and no loose requests), that folder
    /// is used as-is; otherwise a wrapper folder is created named from <paramref name="fileNameHint"/>
    /// (typically the .json file name) or the collection info name.
    /// </summary>
    public static FolderDefinition FromCollectionDocumentAsFolder(
        PostmanCollectionDocument document,
        string? fileNameHint = null)
    {
        var folders = new List<FolderDefinition>();
        var requests = new List<RequestDefinition>();
        var childOrder = new List<ChildOrderEntry>();
        ApplyItems(document.Item, folders, requests, childOrder);

        var collectionVariables = ToKeyValueItems(document.Variable).ToList();

        // Single top-level folder → import that folder directly (keeps its name and nesting).
        if (folders.Count == 1 && requests.Count == 0)
        {
            var sole = folders[0];
            if (collectionVariables.Count > 0)
            {
                sole.Variables.InsertRange(0, collectionVariables);
            }

            return sole;
        }

        var folder = new FolderDefinition
        {
            Name = ResolveImportFolderName(document, fileNameHint),
            ChildOrder = childOrder
        };
        folder.Folders.AddRange(folders);
        folder.Requests.AddRange(requests);
        folder.Variables.AddRange(collectionVariables);
        return folder;
    }

    private static readonly string[] ImportNameSuffixes =
    [
        ".postman_collection.json",
        ".postman_collection",
        ".json"
    ];

    /// <summary>
    /// True for drop/import candidates: <c>*.postman_collection</c>,
    /// <c>*.postman_collection.json</c>, or generic <c>*.json</c>.
    /// </summary>
    public static bool IsImportableCollectionFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var name = Path.GetFileName(filePath);
        return name.EndsWith(".postman_collection.json", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".postman_collection", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the file name uses a Postman collection suffix.</summary>
    public static bool IsPostmanCollectionFileName(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var name = Path.GetFileName(filePath);
        return name.EndsWith(".postman_collection.json", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".postman_collection", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when JSON looks like a Postman Collection v2.x document.</summary>
    public static bool LooksLikePostmanJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // RequestExplorer native collections use folders/requests, not Postman item[].
            if (root.TryGetProperty("folders", out _) || root.TryGetProperty("requests", out _))
            {
                return false;
            }

            if (root.TryGetProperty("item", out _))
            {
                return true;
            }

            if (!root.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (info.TryGetProperty("schema", out var schema)
                && schema.ValueKind == JsonValueKind.String
                && schema.GetString() is { } schemaText
                && schemaText.Contains("postman", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return info.TryGetProperty("_postman_id", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string ResolveImportNameFromFilePath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var suffix in ImportNameSuffixes)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^suffix.Length].Trim();
            }
        }

        return Path.GetFileNameWithoutExtension(fileName).Trim();
    }

    private static string ResolveImportFolderName(PostmanCollectionDocument document, string? fileNameHint)
    {
        if (!string.IsNullOrWhiteSpace(fileNameHint))
        {
            return fileNameHint.Trim();
        }

        return ResolveCollectionName(document);
    }

    private static string ResolveCollectionName(PostmanCollectionDocument document) =>
        string.IsNullOrWhiteSpace(document.Info.Name) ? "Imported Collection" : document.Info.Name.Trim();

    public static void ApplyItems(
        IEnumerable<PostmanItem> items,
        List<FolderDefinition> folders,
        List<RequestDefinition> requests)
        => ApplyItems(items, folders, requests, childOrder: null);

    public static void ApplyItems(
        IEnumerable<PostmanItem> items,
        List<FolderDefinition> folders,
        List<RequestDefinition> requests,
        List<ChildOrderEntry>? childOrder)
    {
        foreach (var item in items)
        {
            if (item.Item is not null)
            {
                var folder = new FolderDefinition { Name = item.Name };
                if (Guid.TryParse(item.Id, out var folderId))
                {
                    folder.Id = folderId;
                }

                ApplyItems(item.Item, folder.Folders, folder.Requests, folder.ChildOrder);
                ApplyVariables(item.Variable, folder.Variables);
                folders.Add(folder);
                childOrder?.Add(new ChildOrderEntry { Kind = TreeChildKind.Folder, Id = folder.Id });
            }
            else if (item.Request is not null)
            {
                var request = FromRequestItem(item);
                requests.Add(request);
                childOrder?.Add(new ChildOrderEntry { Kind = TreeChildKind.Request, Id = request.Id });
            }
        }
    }

    private static List<PostmanItem> ToItems(
        List<FolderDefinition> folders,
        List<RequestDefinition> requests,
        List<ChildOrderEntry>? childOrder)
    {
        var items = new List<PostmanItem>();
        var folderById = folders.ToDictionary(f => f.Id);
        var requestById = requests.ToDictionary(r => r.Id);
        var usedFolders = new HashSet<Guid>();
        var usedRequests = new HashSet<Guid>();

        if (childOrder is { Count: > 0 })
        {
            foreach (var entry in childOrder)
            {
                if (entry.Kind == TreeChildKind.Folder
                    && folderById.TryGetValue(entry.Id, out var folder)
                    && usedFolders.Add(folder.Id))
                {
                    items.Add(ToFolderItem(folder));
                }
                else if (entry.Kind == TreeChildKind.Request
                         && requestById.TryGetValue(entry.Id, out var request)
                         && usedRequests.Add(request.Id))
                {
                    items.Add(ToRequestItem(request));
                }
            }
        }

        foreach (var folder in folders)
        {
            if (usedFolders.Add(folder.Id))
            {
                items.Add(ToFolderItem(folder));
            }
        }

        foreach (var request in requests)
        {
            if (usedRequests.Add(request.Id))
            {
                items.Add(ToRequestItem(request));
            }
        }

        return items;
    }

    private static PostmanItem ToFolderItem(FolderDefinition folder) =>
        new()
        {
            Id = folder.Id.ToString(),
            Name = folder.Name,
            Variable = ToPostmanVariables(folder.Variables),
            Item = ToItems(folder.Folders, folder.Requests, folder.ChildOrder)
        };

    private static List<PostmanKeyValue>? ToPostmanVariables(IEnumerable<KeyValueItem> variables)
    {
        var list = variables
            .Where(v => !string.IsNullOrWhiteSpace(v.Key))
            .Select(v => new PostmanKeyValue
            {
                Key = v.Key,
                Value = v.Value,
                Disabled = v.IsEnabled ? null : true,
                Type = "string"
            })
            .ToList();
        return list.Count == 0 ? null : list;
    }

    public static IEnumerable<KeyValueItem> ToKeyValueItems(IEnumerable<PostmanKeyValue>? source)
    {
        if (source is null)
        {
            yield break;
        }

        foreach (var item in source)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
            {
                continue;
            }

            yield return new KeyValueItem
            {
                Key = item.Key ?? string.Empty,
                Value = item.Value ?? string.Empty,
                IsEnabled = item.Disabled != true
            };
        }
    }

    private static void ApplyVariables(IEnumerable<PostmanKeyValue>? source, List<KeyValueItem> target)
    {
        foreach (var item in ToKeyValueItems(source))
        {
            target.Add(item);
        }
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
            Id = request.Id.ToString(),
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

        if (Guid.TryParse(item.Id, out var requestId))
        {
            request.Id = requestId;
        }

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

        // Parse field-by-field so "url" may be a string or an object (common Postman shapes).
        if (element.TryGetProperty("method", out var methodEl)
            && methodEl.ValueKind == JsonValueKind.String
            && Enum.TryParse<HttpVerb>(methodEl.GetString(), ignoreCase: true, out var verb))
        {
            request.Verb = verb;
        }

        ApplyUrl(request, element);
        ApplyHeaders(request, element);
        ApplyAuthFromElement(request, element);
        ApplyBodyFromElement(request, element);

        return request;
    }

    private static void ApplyUrl(RequestDefinition request, JsonElement requestElement)
    {
        if (!requestElement.TryGetProperty("url", out var urlEl))
        {
            return;
        }

        PostmanUrl? url = null;
        if (urlEl.ValueKind == JsonValueKind.String)
        {
            request.Address = urlEl.GetString() ?? string.Empty;
            return;
        }

        if (urlEl.ValueKind == JsonValueKind.Object)
        {
            url = urlEl.Deserialize<PostmanUrl>(JsonOptions);
        }

        if (url is null)
        {
            return;
        }

        request.Address = url.Raw ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.Address))
        {
            request.Address = ReconstructRawUrl(url);
        }

        if (url.Query is not { Count: > 0 })
        {
            return;
        }

        foreach (var query in url.Query)
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

    private static void ApplyHeaders(RequestDefinition request, JsonElement requestElement)
    {
        if (!requestElement.TryGetProperty("header", out var headerEl)
            || headerEl.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var header in headerEl.EnumerateArray())
        {
            var kv = header.Deserialize<PostmanKeyValue>(JsonOptions);
            if (kv is null || string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            request.Headers.Add(new KeyValueItem
            {
                Key = kv.Key ?? string.Empty,
                Value = kv.Value ?? string.Empty,
                IsEnabled = kv.Disabled != true
            });
        }
    }

    private static void ApplyAuthFromElement(RequestDefinition request, JsonElement requestElement)
    {
        if (!requestElement.TryGetProperty("auth", out var authEl)
            || authEl.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        var auth = authEl.Deserialize<PostmanAuth>(JsonOptions);
        ApplyAuth(request, auth);
    }

    private static void ApplyBodyFromElement(RequestDefinition request, JsonElement requestElement)
    {
        if (!requestElement.TryGetProperty("body", out var bodyEl)
            || bodyEl.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var body = bodyEl.Deserialize<PostmanBody>(JsonOptions);
        if (body is not { Mode: "raw" })
        {
            return;
        }

        request.Body.Content = body.Raw ?? string.Empty;
        var language = body.Options?.Raw?.Language;
        request.Body.ContentType = MapLanguageToContentType(language);

        // Prefer an explicit Content-Type header when present (enabled).
        var contentTypeHeader = request.Headers.FirstOrDefault(h =>
            h.IsEnabled && h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
        if (contentTypeHeader is not null && !string.IsNullOrWhiteSpace(contentTypeHeader.Value))
        {
            request.Body.ContentType = contentTypeHeader.Value;
        }
    }

    private static PostmanUrl BuildUrl(RequestDefinition request)
    {
        var allParams = request.Params
            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .ToList();
        var enabledParams = allParams.Where(p => p.IsEnabled).ToList();

        var raw = request.Address?.Trim() ?? string.Empty;
        if (enabledParams.Count > 0)
        {
            var query = string.Join("&", enabledParams.Select(p =>
                $"{VariableResolver.EscapeDataStringPreservingTokens(p.Key)}={VariableResolver.EscapeDataStringPreservingTokens(p.Value ?? string.Empty)}"));
            var qIndex = raw.IndexOf('?', StringComparison.Ordinal);
            raw = qIndex >= 0 ? $"{raw[..qIndex]}?{query}" : $"{raw}?{query}";
        }

        var url = new PostmanUrl { Raw = raw };
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            if (allParams.Count > 0)
            {
                url.Query = allParams.Select(ToQueryKeyValue).ToList();
            }

            return url;
        }

        url.Protocol = uri.Scheme;
        url.Host = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
        url.Path = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (allParams.Count > 0)
        {
            url.Query = allParams.Select(ToQueryKeyValue).ToList();
        }
        else if (!string.IsNullOrEmpty(uri.Query))
        {
            url.Query = ParseQuery(uri.Query);
        }

        return url;
    }

    private static PostmanKeyValue ToQueryKeyValue(KeyValueItem p) =>
        new()
        {
            Key = p.Key,
            Value = p.Value,
            Disabled = p.IsEnabled ? null : true
        };

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
                .Select(q => $"{VariableResolver.EscapeDataStringPreservingTokens(q.Key!)}={VariableResolver.EscapeDataStringPreservingTokens(q.Value ?? string.Empty)}"))
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

        return new PostmanBody
        {
            Mode = "raw",
            Raw = request.Body.Content,
            Options = new PostmanBodyOptions
            {
                Raw = new PostmanRawBodyOptions
                {
                    Language = MapContentTypeToLanguage(request.Body.ContentType)
                }
            }
        };
    }

    internal static string MapContentTypeToLanguage(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return "text";
        }

        var ct = contentType.Trim();
        if (ct.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return "json";
        }

        if (ct.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            return "xml";
        }

        if (ct.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return "html";
        }

        if (ct.Contains("javascript", StringComparison.OrdinalIgnoreCase)
            || ct.Equals("text/js", StringComparison.OrdinalIgnoreCase))
        {
            return "javascript";
        }

        return "text";
    }

    internal static string MapLanguageToContentType(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "text/plain";
        }

        return language.Trim().ToLowerInvariant() switch
        {
            "json" => "application/json",
            "xml" => "application/xml",
            "html" => "text/html",
            "javascript" or "js" => "application/javascript",
            _ => "text/plain"
        };
    }
}
