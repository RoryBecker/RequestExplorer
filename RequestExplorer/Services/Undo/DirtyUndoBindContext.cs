using System.Text.Json;
using RequestExplorer.Models;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class DirtyUndoBindContext
{
    private readonly Dictionary<Guid, TreeNodeViewModel> _nodes = [];
    private readonly Dictionary<Guid, KeyValueItemViewModel> _items = [];

    public required Func<Guid, TreeNodeViewModel?> FindNode { get; init; }

    public required Func<string, JsonElement, TreeNodeViewModel?> CreateNode { get; init; }

    public required Action<TreeNodeViewModel> PrepareNode { get; init; }

    public required Func<RequestNodeViewModel, KeyValueListKind, KeyValueItem, KeyValueItemViewModel> CreateKeyValue { get; init; }

    public required Func<ContainerNodeViewModel, KeyValueItem, KeyValueItemViewModel> CreateVariable { get; init; }

    public TreeNodeViewModel? ResolveNode(
        Guid? id,
        string? nodeKind = null,
        JsonElement? nodeModel = null)
    {
        if (id is Guid nodeId)
        {
            if (_nodes.TryGetValue(nodeId, out var cached))
            {
                return cached;
            }

            var found = FindNode(nodeId);
            if (found is not null)
            {
                _nodes[nodeId] = found;
                return found;
            }
        }

        if (nodeKind is null || nodeModel is not JsonElement model)
        {
            return null;
        }

        var created = CreateNode(nodeKind, model);
        if (created is null)
        {
            return null;
        }

        PrepareNode(created);
        if (TryGetId(created) is Guid createdId)
        {
            _nodes[createdId] = created;
        }

        return created;
    }

    public KeyValueItemViewModel? ResolveListItem(
        RequestNodeViewModel request,
        KeyValueListKind listKind,
        DirtyUndoActionDto dto)
    {
        if (dto.ItemId is Guid itemId && _items.TryGetValue(itemId, out var cached))
        {
            return cached;
        }

        var list = listKind == KeyValueListKind.Params ? request.Params : request.Headers;
        var index = dto.ItemIndex ?? -1;
        if (index >= 0 && index < list.Count)
        {
            var item = list[index];
            CacheItem(dto.ItemId, item);
            return item;
        }

        return ResolveOrCreateListItem(request, listKind, dto);
    }

    public KeyValueItemViewModel? ResolveOrCreateListItem(
        RequestNodeViewModel request,
        KeyValueListKind listKind,
        DirtyUndoActionDto dto)
    {
        if (dto.ItemId is Guid itemId && _items.TryGetValue(itemId, out var cached))
        {
            return cached;
        }

        if (dto.Item is null)
        {
            return null;
        }

        var item = CreateKeyValue(request, listKind, ToModel(dto.Item));
        CacheItem(dto.ItemId, item);
        return item;
    }

    public KeyValueItemViewModel? ResolveVariableItem(
        ContainerNodeViewModel container,
        DirtyUndoActionDto dto)
    {
        if (dto.ItemId is Guid itemId && _items.TryGetValue(itemId, out var cached))
        {
            return cached;
        }

        var index = dto.ItemIndex ?? -1;
        if (index >= 0 && index < container.Variables.Count)
        {
            var item = container.Variables[index];
            CacheItem(dto.ItemId, item);
            return item;
        }

        return ResolveOrCreateVariableItem(container, dto);
    }

    public KeyValueItemViewModel? ResolveOrCreateVariableItem(
        ContainerNodeViewModel container,
        DirtyUndoActionDto dto)
    {
        if (dto.ItemId is Guid itemId && _items.TryGetValue(itemId, out var cached))
        {
            return cached;
        }

        if (dto.Item is null)
        {
            return null;
        }

        var item = CreateVariable(container, ToModel(dto.Item));
        CacheItem(dto.ItemId, item);
        return item;
    }

    private void CacheItem(Guid? itemId, KeyValueItemViewModel item)
    {
        if (itemId is Guid id)
        {
            _items[id] = item;
        }
    }

    private static KeyValueItem ToModel(KeyValueItemSnapshot snapshot) =>
        new()
        {
            IsEnabled = snapshot.IsEnabled,
            Key = snapshot.Key,
            Value = snapshot.Value,
            IsSecret = snapshot.IsSecret
        };

    private static Guid? TryGetId(TreeNodeViewModel node) =>
        node switch
        {
            CollectionNodeViewModel collection => collection.Model.Id,
            FolderNodeViewModel folder => folder.Model.Id,
            RequestNodeViewModel request => request.Model.Id,
            _ => null
        };
}
