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
        var idMap = new Dictionary<Guid, Guid>();
        var oldCollectionId = clone.Id;
        clone.Id = Guid.NewGuid();
        idMap[oldCollectionId] = clone.Id;
        foreach (var folder in clone.Folders)
        {
            AssignNewIds(folder, idMap);
        }

        foreach (var request in clone.Requests)
        {
            var oldId = request.Id;
            request.Id = Guid.NewGuid();
            idMap[oldId] = request.Id;
        }

        RemapChildOrder(clone.ChildOrder, idMap);
        return clone;
    }

    /// <summary>Paste a copied collection into a folder/collection as a single folder.</summary>
    public static FolderDefinition CollectionAsFolder(CollectionDefinition collection)
    {
        var folder = new FolderDefinition
        {
            Id = Guid.NewGuid(),
            Name = collection.Name,
            Variables = collection.Variables
                .Select(v => new KeyValueItem
                {
                    IsEnabled = v.IsEnabled,
                    Key = v.Key,
                    Value = v.Value
                })
                .ToList()
        };

        AppendClonedChildren(
            folder.Folders,
            folder.Requests,
            folder.ChildOrder,
            collection.Folders,
            collection.Requests,
            collection.ChildOrder);

        return folder;
    }

    public static CollectionDefinition WrapInNewCollection(FolderDefinition folder)
    {
        var clone = CloneFolder(folder);
        return new CollectionDefinition
        {
            Id = Guid.NewGuid(),
            Name = clone.Name,
            Variables = clone.Variables,
            Folders = clone.Folders,
            Requests = clone.Requests,
            ChildOrder = clone.ChildOrder
        };
    }

    public static CollectionDefinition WrapInNewCollection(RequestDefinition request)
    {
        var wrapped = CloneRequest(request);
        return new CollectionDefinition
        {
            Id = Guid.NewGuid(),
            Name = wrapped.Name,
            Requests = [wrapped],
            ChildOrder =
            [
                new ChildOrderEntry { Kind = TreeChildKind.Request, Id = wrapped.Id }
            ]
        };
    }

    private static void AppendClonedChildren(
        List<FolderDefinition> targetFolders,
        List<RequestDefinition> targetRequests,
        List<ChildOrderEntry> targetOrder,
        List<FolderDefinition> sourceFolders,
        List<RequestDefinition> sourceRequests,
        List<ChildOrderEntry>? sourceOrder)
    {
        var folderById = sourceFolders.ToDictionary(f => f.Id);
        var requestById = sourceRequests.ToDictionary(r => r.Id);
        var usedFolders = new HashSet<Guid>();
        var usedRequests = new HashSet<Guid>();

        void AddFolder(FolderDefinition source)
        {
            if (!usedFolders.Add(source.Id))
            {
                return;
            }

            var clone = CloneFolder(source);
            targetFolders.Add(clone);
            targetOrder.Add(new ChildOrderEntry { Kind = TreeChildKind.Folder, Id = clone.Id });
        }

        void AddRequest(RequestDefinition source)
        {
            if (!usedRequests.Add(source.Id))
            {
                return;
            }

            var clone = CloneRequest(source);
            targetRequests.Add(clone);
            targetOrder.Add(new ChildOrderEntry { Kind = TreeChildKind.Request, Id = clone.Id });
        }

        if (sourceOrder is { Count: > 0 })
        {
            foreach (var entry in sourceOrder)
            {
                if (entry.Kind == TreeChildKind.Folder && folderById.TryGetValue(entry.Id, out var folder))
                {
                    AddFolder(folder);
                }
                else if (entry.Kind == TreeChildKind.Request && requestById.TryGetValue(entry.Id, out var request))
                {
                    AddRequest(request);
                }
            }
        }

        foreach (var folder in sourceFolders)
        {
            AddFolder(folder);
        }

        foreach (var request in sourceRequests)
        {
            AddRequest(request);
        }
    }

    private static void AssignNewIds(FolderDefinition folder) =>
        AssignNewIds(folder, new Dictionary<Guid, Guid>());

    private static void AssignNewIds(FolderDefinition folder, Dictionary<Guid, Guid> idMap)
    {
        var oldId = folder.Id;
        folder.Id = Guid.NewGuid();
        idMap[oldId] = folder.Id;

        foreach (var child in folder.Folders)
        {
            AssignNewIds(child, idMap);
        }

        foreach (var request in folder.Requests)
        {
            var oldRequestId = request.Id;
            request.Id = Guid.NewGuid();
            idMap[oldRequestId] = request.Id;
        }

        RemapChildOrder(folder.ChildOrder, idMap);
    }

    private static void RemapChildOrder(List<ChildOrderEntry>? order, IReadOnlyDictionary<Guid, Guid> idMap)
    {
        if (order is null || order.Count == 0)
        {
            return;
        }

        foreach (var entry in order)
        {
            if (idMap.TryGetValue(entry.Id, out var newId))
            {
                entry.Id = newId;
            }
        }
    }

    private static T DeepClone<T>(T source) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;
}
