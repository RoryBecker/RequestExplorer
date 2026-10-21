using System.Text.Json;
using System.Text.Json.Serialization;
using RequestExplorer.Models;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Tree;

/// <summary>Serialize / deserialize internal RequestExplorer subtree JSON (collection, folder, or request).</summary>
public static class SubtreeJsonService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions FormatOptions = new()
    {
        WriteIndented = true
    };

    public static string Serialize(TreeNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node switch
        {
            CollectionNodeViewModel collection =>
                JsonSerializer.Serialize(collection.Model, JsonOptions),
            FolderNodeViewModel folder =>
                JsonSerializer.Serialize(folder.Model, JsonOptions),
            RequestNodeViewModel request =>
                JsonSerializer.Serialize(request.Model, JsonOptions),
            _ => throw new ArgumentException($"Unsupported node kind '{node.Kind}'.", nameof(node))
        };
    }

    public static bool TryFormat(string json, out string formatted, out string? error)
    {
        formatted = json;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            formatted = JsonSerializer.Serialize(document.RootElement, FormatOptions);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryDeserialize(
        TreeNodeKind kind,
        string json,
        Guid preserveRootId,
        out object? model,
        out string? error)
    {
        model = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "JSON is empty.";
            return false;
        }

        try
        {
            switch (kind)
            {
                case TreeNodeKind.Collection:
                {
                    var collection = JsonSerializer.Deserialize<CollectionDefinition>(json, JsonOptions);
                    if (collection is null)
                    {
                        error = "JSON did not deserialize to a collection.";
                        return false;
                    }

                    Normalize(collection);
                    collection.Id = preserveRootId;
                    model = collection;
                    return true;
                }
                case TreeNodeKind.Folder:
                {
                    var folder = JsonSerializer.Deserialize<FolderDefinition>(json, JsonOptions);
                    if (folder is null)
                    {
                        error = "JSON did not deserialize to a folder.";
                        return false;
                    }

                    Normalize(folder);
                    folder.Id = preserveRootId;
                    model = folder;
                    return true;
                }
                case TreeNodeKind.Request:
                {
                    var request = JsonSerializer.Deserialize<RequestDefinition>(json, JsonOptions);
                    if (request is null)
                    {
                        error = "JSON did not deserialize to a request.";
                        return false;
                    }

                    Normalize(request);
                    request.Id = preserveRootId;
                    model = request;
                    return true;
                }
                default:
                    error = $"Unsupported node kind '{kind}'.";
                    return false;
            }
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (NotSupportedException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void Normalize(CollectionDefinition collection)
    {
        collection.Name = string.IsNullOrWhiteSpace(collection.Name) ? "Collection" : collection.Name.Trim();
        collection.Variables ??= [];
        collection.Folders ??= [];
        collection.Requests ??= [];
        collection.ChildOrder ??= [];
        foreach (var folder in collection.Folders)
        {
            Normalize(folder);
        }

        foreach (var request in collection.Requests)
        {
            Normalize(request);
        }

        foreach (var variable in collection.Variables)
        {
            Normalize(variable);
        }

        CollectionTreeMapper.EnsureChildOrder(collection);
    }

    private static void Normalize(FolderDefinition folder)
    {
        folder.Name = string.IsNullOrWhiteSpace(folder.Name) ? "Folder" : folder.Name.Trim();
        folder.Variables ??= [];
        folder.Folders ??= [];
        folder.Requests ??= [];
        folder.ChildOrder ??= [];
        foreach (var child in folder.Folders)
        {
            Normalize(child);
        }

        foreach (var request in folder.Requests)
        {
            Normalize(request);
        }

        foreach (var variable in folder.Variables)
        {
            Normalize(variable);
        }

        CollectionTreeMapper.EnsureChildOrder(folder);
    }

    private static void Normalize(RequestDefinition request)
    {
        request.Name = string.IsNullOrWhiteSpace(request.Name) ? "Request" : request.Name.Trim();
        request.Address ??= string.Empty;
        request.Params ??= [];
        request.Headers ??= [];
        request.Authentication ??= new AuthenticationSettings();
        request.Body ??= new RequestBody();
        if (request.ExpectedStatusCode is < 100 or > 599)
        {
            request.ExpectedStatusCode = 200;
        }

        request.ExpectedBody ??= string.Empty;
        // Unset / legacy timing expectation → on at 300ms.
        if (!request.ExpectTimingEnabled && request.ExpectedTimingMilliseconds <= 0)
        {
            request.ExpectTimingEnabled = true;
            request.ExpectedTimingMilliseconds = 300;
        }
        else if (request.ExpectedTimingMilliseconds < 0)
        {
            request.ExpectedTimingMilliseconds = 300;
        }

        foreach (var item in request.Params)
        {
            Normalize(item);
        }

        foreach (var item in request.Headers)
        {
            Normalize(item);
        }
    }

    private static void Normalize(KeyValueItem item)
    {
        item.Key ??= string.Empty;
        item.Value ??= string.Empty;
    }
}
