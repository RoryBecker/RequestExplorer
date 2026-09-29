using System.Text.Json;
using System.Text.Json.Serialization;
using RequestExplorer.Models;

namespace RequestExplorer.Services;

public static class TreeNodeCloneService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static RequestDefinition CloneRequest(RequestDefinition source)
    {
        var clone = DeepClone(source);
        clone.Id = Guid.NewGuid();
        return clone;
    }

    public static FolderDefinition CloneFolder(FolderDefinition source)
    {
        var clone = DeepClone(source);
        AssignNewIds(clone);
        return clone;
    }

    public static CollectionDefinition CloneCollection(CollectionDefinition source)
    {
        var clone = DeepClone(source);
        clone.Id = Guid.NewGuid();
        foreach (var folder in clone.Folders)
        {
            AssignNewIds(folder);
        }

        foreach (var request in clone.Requests)
        {
            request.Id = Guid.NewGuid();
        }

        return clone;
    }

    /// <summary>Paste a copied collection into a folder/collection as a single folder.</summary>
    public static FolderDefinition CollectionAsFolder(CollectionDefinition collection)
    {
        var folder = new FolderDefinition
        {
            Id = Guid.NewGuid(),
            Name = collection.Name
        };

        foreach (var childFolder in collection.Folders)
        {
            folder.Folders.Add(CloneFolder(childFolder));
        }

        foreach (var request in collection.Requests)
        {
            folder.Requests.Add(CloneRequest(request));
        }

        return folder;
    }

    public static CollectionDefinition WrapInNewCollection(FolderDefinition folder)
    {
        var clone = CloneFolder(folder);
        return new CollectionDefinition
        {
            Id = Guid.NewGuid(),
            Name = clone.Name,
            Folders = clone.Folders,
            Requests = clone.Requests
        };
    }

    public static CollectionDefinition WrapInNewCollection(RequestDefinition request)
    {
        var wrapped = CloneRequest(request);
        return new CollectionDefinition
        {
            Id = Guid.NewGuid(),
            Name = wrapped.Name,
            Requests = [wrapped]
        };
    }

    private static void AssignNewIds(FolderDefinition folder)
    {
        folder.Id = Guid.NewGuid();
        foreach (var child in folder.Folders)
        {
            AssignNewIds(child);
        }

        foreach (var request in folder.Requests)
        {
            request.Id = Guid.NewGuid();
        }
    }

    private static T DeepClone<T>(T source) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;
}
