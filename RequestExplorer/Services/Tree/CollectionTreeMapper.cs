using RequestExplorer.Models;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Tree;

public static class CollectionTreeMapper
{
    public static CollectionNodeViewModel ToViewModel(CollectionDefinition collection)
    {
        var node = new CollectionNodeViewModel(collection);
        AddChildrenInOrder(
            node,
            collection.Folders,
            collection.Requests,
            collection.ChildOrder,
            ToFolderViewModel,
            r => new RequestNodeViewModel(r));
        node.IsExpanded = true;
        return node;
    }

    public static void SyncModelFromView(CollectionNodeViewModel collectionNode)
    {
        var model = collectionNode.Model;
        model.Name = collectionNode.Name;
        model.Folders.Clear();
        model.Requests.Clear();
        model.ChildOrder.Clear();

        foreach (var child in collectionNode.Children)
        {
            switch (child)
            {
                case FolderNodeViewModel folder:
                    SyncFolder(folder);
                    model.Folders.Add(folder.Model);
                    model.ChildOrder.Add(new ChildOrderEntry
                    {
                        Kind = TreeChildKind.Folder,
                        Id = folder.Model.Id
                    });
                    break;
                case RequestNodeViewModel request:
                    model.Requests.Add(request.Model);
                    model.ChildOrder.Add(new ChildOrderEntry
                    {
                        Kind = TreeChildKind.Request,
                        Id = request.Model.Id
                    });
                    break;
            }
        }
    }

    public static void ClearDirty(TreeNodeViewModel node)
    {
        if (node is RequestNodeViewModel request)
        {
            request.ClearDirty();
        }
        else
        {
            node.IsDirty = false;
        }

        foreach (var child in node.Children)
        {
            ClearDirty(child);
        }
    }

    public static bool HasDirty(TreeNodeViewModel node) =>
        node.IsDirty || node.Children.Any(HasDirty);

    public static CollectionNodeViewModel? FindRootCollection(TreeNodeViewModel? node)
    {
        while (node is not null)
        {
            if (node is CollectionNodeViewModel collection)
            {
                return collection;
            }

            node = node.Parent;
        }

        return null;
    }

    public static FolderNodeViewModel ToFolderViewModel(FolderDefinition folder)
    {
        var node = new FolderNodeViewModel(folder);
        AddChildrenInOrder(
            node,
            folder.Folders,
            folder.Requests,
            folder.ChildOrder,
            ToFolderViewModel,
            r => new RequestNodeViewModel(r));
        node.IsExpanded = true;
        return node;
    }

    public static void SyncFolder(FolderNodeViewModel folderNode)
    {
        var model = folderNode.Model;
        model.Name = folderNode.Name;
        model.Folders.Clear();
        model.Requests.Clear();
        model.ChildOrder.Clear();

        foreach (var child in folderNode.Children)
        {
            switch (child)
            {
                case FolderNodeViewModel folder:
                    SyncFolder(folder);
                    model.Folders.Add(folder.Model);
                    model.ChildOrder.Add(new ChildOrderEntry
                    {
                        Kind = TreeChildKind.Folder,
                        Id = folder.Model.Id
                    });
                    break;
                case RequestNodeViewModel request:
                    model.Requests.Add(request.Model);
                    model.ChildOrder.Add(new ChildOrderEntry
                    {
                        Kind = TreeChildKind.Request,
                        Id = request.Model.Id
                    });
                    break;
            }
        }
    }

    /// <summary>
    /// Rebuilds <see cref="ChildOrderEntry"/> lists from current Folders/Requests membership
    /// when order is unknown (keeps existing ChildOrder entries that still resolve).
    /// </summary>
    public static void EnsureChildOrder(CollectionDefinition collection)
    {
        collection.ChildOrder = NormalizeChildOrder(
            collection.ChildOrder,
            collection.Folders,
            collection.Requests);
        foreach (var folder in collection.Folders)
        {
            EnsureChildOrder(folder);
        }
    }

    public static void EnsureChildOrder(FolderDefinition folder)
    {
        folder.ChildOrder = NormalizeChildOrder(
            folder.ChildOrder,
            folder.Folders,
            folder.Requests);
        foreach (var child in folder.Folders)
        {
            EnsureChildOrder(child);
        }
    }

    private static void AddChildrenInOrder(
        TreeNodeViewModel parent,
        List<FolderDefinition> folders,
        List<RequestDefinition> requests,
        List<ChildOrderEntry>? childOrder,
        Func<FolderDefinition, FolderNodeViewModel> mapFolder,
        Func<RequestDefinition, RequestNodeViewModel> mapRequest)
    {
        var folderById = folders.ToDictionary(f => f.Id);
        var requestById = requests.ToDictionary(r => r.Id);
        var usedFolders = new HashSet<Guid>();
        var usedRequests = new HashSet<Guid>();

        if (childOrder is { Count: > 0 })
        {
            foreach (var entry in childOrder)
            {
                switch (entry.Kind)
                {
                    case TreeChildKind.Folder when folderById.TryGetValue(entry.Id, out var folder):
                        if (usedFolders.Add(folder.Id))
                        {
                            parent.AddChild(mapFolder(folder));
                        }

                        break;
                    case TreeChildKind.Request when requestById.TryGetValue(entry.Id, out var request):
                        if (usedRequests.Add(request.Id))
                        {
                            parent.AddChild(mapRequest(request));
                        }

                        break;
                }
            }
        }

        foreach (var folder in folders)
        {
            if (usedFolders.Add(folder.Id))
            {
                parent.AddChild(mapFolder(folder));
            }
        }

        foreach (var request in requests)
        {
            if (usedRequests.Add(request.Id))
            {
                parent.AddChild(mapRequest(request));
            }
        }
    }

    private static List<ChildOrderEntry> NormalizeChildOrder(
        List<ChildOrderEntry>? existing,
        List<FolderDefinition> folders,
        List<RequestDefinition> requests)
    {
        var folderIds = folders.Select(f => f.Id).ToHashSet();
        var requestIds = requests.Select(r => r.Id).ToHashSet();
        var result = new List<ChildOrderEntry>();
        var usedFolders = new HashSet<Guid>();
        var usedRequests = new HashSet<Guid>();

        if (existing is { Count: > 0 })
        {
            foreach (var entry in existing)
            {
                if (entry.Kind == TreeChildKind.Folder
                    && folderIds.Contains(entry.Id)
                    && usedFolders.Add(entry.Id))
                {
                    result.Add(entry);
                }
                else if (entry.Kind == TreeChildKind.Request
                         && requestIds.Contains(entry.Id)
                         && usedRequests.Add(entry.Id))
                {
                    result.Add(entry);
                }
            }
        }

        foreach (var folder in folders)
        {
            if (usedFolders.Add(folder.Id))
            {
                result.Add(new ChildOrderEntry { Kind = TreeChildKind.Folder, Id = folder.Id });
            }
        }

        foreach (var request in requests)
        {
            if (usedRequests.Add(request.Id))
            {
                result.Add(new ChildOrderEntry { Kind = TreeChildKind.Request, Id = request.Id });
            }
        }

        return result;
    }
}
