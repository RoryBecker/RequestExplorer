using RequestExplorer.Models;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Services;

public static class CollectionTreeMapper
{
    public static CollectionNodeViewModel ToViewModel(CollectionDefinition collection)
    {
        var node = new CollectionNodeViewModel(collection);

        foreach (var folder in collection.Folders)
        {
            node.AddChild(ToFolderViewModel(folder));
        }

        foreach (var request in collection.Requests)
        {
            node.AddChild(new RequestNodeViewModel(request));
        }

        node.IsExpanded = true;
        return node;
    }

    public static void SyncModelFromView(CollectionNodeViewModel collectionNode)
    {
        var model = collectionNode.Model;
        model.Name = collectionNode.Name;
        model.Folders.Clear();
        model.Requests.Clear();

        foreach (var child in collectionNode.Children)
        {
            switch (child)
            {
                case FolderNodeViewModel folder:
                    SyncFolder(folder);
                    model.Folders.Add(folder.Model);
                    break;
                case RequestNodeViewModel request:
                    model.Requests.Add(request.Model);
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

        foreach (var child in node.Children)
        {
            ClearDirty(child);
        }
    }

    public static bool HasDirty(TreeNodeViewModel node) =>
        node is RequestNodeViewModel { IsDirty: true }
        || node.Children.Any(HasDirty);

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

    private static FolderNodeViewModel ToFolderViewModel(FolderDefinition folder)
    {
        var node = new FolderNodeViewModel(folder);

        foreach (var child in folder.Folders)
        {
            node.AddChild(ToFolderViewModel(child));
        }

        foreach (var request in folder.Requests)
        {
            node.AddChild(new RequestNodeViewModel(request));
        }

        node.IsExpanded = true;
        return node;
    }

    private static void SyncFolder(FolderNodeViewModel folderNode)
    {
        var model = folderNode.Model;
        model.Name = folderNode.Name;
        model.Folders.Clear();
        model.Requests.Clear();

        foreach (var child in folderNode.Children)
        {
            switch (child)
            {
                case FolderNodeViewModel folder:
                    SyncFolder(folder);
                    model.Folders.Add(folder.Model);
                    break;
                case RequestNodeViewModel request:
                    model.Requests.Add(request.Model);
                    break;
            }
        }
    }
}
