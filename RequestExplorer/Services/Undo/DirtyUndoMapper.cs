using System.Text.Json;
using System.Text.Json.Serialization;
using RequestExplorer.Models;
using RequestExplorer.Services.Tree;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public static class DirtyUndoMapper
{
    public const string SetName = "SetName";
    public const string SetRequestBodyField = "SetRequestBodyField";
    public const string SetRequestAddress = "SetRequestAddress";
    public const string SetKeyValueField = "SetKeyValueField";
    public const string SetVariableField = "SetVariableField";
    public const string InsertNode = "InsertNode";
    public const string RemoveNode = "RemoveNode";
    public const string InsertKeyValueRow = "InsertKeyValueRow";
    public const string RemoveKeyValueRow = "RemoveKeyValueRow";
    public const string InsertVariableRow = "InsertVariableRow";
    public const string RemoveVariableRow = "RemoveVariableRow";
    public const string Composite = "Composite";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static DirtyUndoStackSnapshot Serialize(
        IReadOnlyList<IUndoAction> undoOldestFirst,
        IReadOnlyList<IUndoAction> redoOldestFirst)
    {
        var items = new Dictionary<KeyValueItemViewModel, Guid>();
        return new DirtyUndoStackSnapshot
        {
            Undo = SerializeActions(undoOldestFirst, items),
            Redo = SerializeActions(redoOldestFirst, items)
        };
    }

    public static DirtyUndoStackSnapshot Serialize(
        IReadOnlyList<IUndoAction> undoOldestFirst,
        IReadOnlyList<IUndoAction> redoOldestFirst,
        long nextHistoryId)
    {
        var snapshot = Serialize(undoOldestFirst, redoOldestFirst);
        snapshot.NextHistoryId = nextHistoryId;
        return snapshot;
    }

    public static bool TryDeserialize(
        DirtyUndoStackSnapshot snapshot,
        DirtyUndoBindContext context,
        out List<IUndoAction> undoOldestFirst,
        out List<IUndoAction> redoOldestFirst)
    {
        undoOldestFirst = [];
        redoOldestFirst = [];
        if (snapshot is null)
        {
            return false;
        }

        foreach (var dto in snapshot.Undo)
        {
            if (TryCreateAction(dto, context, out var action))
            {
                undoOldestFirst.Add(action);
            }
        }

        foreach (var dto in snapshot.Redo)
        {
            if (TryCreateAction(dto, context, out var action))
            {
                redoOldestFirst.Add(action);
            }
        }

        return true;
    }

    private static List<DirtyUndoActionDto> SerializeActions(
        IReadOnlyList<IUndoAction> actions,
        Dictionary<KeyValueItemViewModel, Guid> items)
    {
        var list = new List<DirtyUndoActionDto>(actions.Count);
        foreach (var action in actions)
        {
            var dto = SerializeAction(action, items);
            if (dto is not null)
            {
                dto.HistoryId = action.HistoryId;
                list.Add(dto);
            }
        }

        return list;
    }

    private static DirtyUndoActionDto? SerializeAction(
        IUndoAction action,
        Dictionary<KeyValueItemViewModel, Guid> items)
    {
        switch (action)
        {
            case CompositeUndoAction composite:
                var steps = new List<DirtyUndoActionDto>();
                foreach (var step in composite.Steps)
                {
                    var stepDto = SerializeAction(step, items);
                    if (stepDto is null)
                    {
                        return null;
                    }

                    steps.Add(stepDto);
                }

                return new DirtyUndoActionDto
                {
                    Type = Composite,
                    Description = composite.Description,
                    Steps = steps,
                    SelectAfter = ToNodeIds(composite.SelectAfter),
                    SelectAfterInverse = ToNodeIds(composite.SelectAfterInverse)
                };

            case SetNameAction setName:
                return new DirtyUndoActionDto
                {
                    Type = SetName,
                    Description = setName.Description,
                    NodeId = TryGetId(setName.Node),
                    StringValue = setName.Name
                };

            case SetRequestBodyFieldAction body:
                return new DirtyUndoActionDto
                {
                    Type = SetRequestBodyField,
                    Description = body.Description,
                    RequestId = TryGetId(body.Request),
                    Field = body.Field.ToString(),
                    StringValue = body.Value
                };

            case SetRequestAddressAction address:
                return new DirtyUndoActionDto
                {
                    Type = SetRequestAddress,
                    Description = address.Description,
                    RequestId = TryGetId(address.Request),
                    StringValue = address.Address
                };

            case SetKeyValueFieldAction kv:
                return new DirtyUndoActionDto
                {
                    Type = SetKeyValueField,
                    Description = kv.Description,
                    RequestId = TryGetId(kv.Request),
                    ListKind = kv.ListKind.ToString(),
                    Field = kv.Field.ToString(),
                    ItemId = GetItemId(kv.Item, items),
                    ItemIndex = kv.Request.IndexOfKeyValue(kv.ListKind, kv.Item),
                    Item = SnapshotItem(kv.Item),
                    BoolValue = kv.Value is bool kvBool ? kvBool : null,
                    StringValue = kv.Value as string
                };

            case SetVariableFieldAction variable:
                return new DirtyUndoActionDto
                {
                    Type = SetVariableField,
                    Description = variable.Description,
                    ContainerId = TryGetId(variable.Container),
                    Field = variable.Field.ToString(),
                    ItemId = GetItemId(variable.Item, items),
                    ItemIndex = variable.Container.IndexOfVariable(variable.Item),
                    Item = SnapshotItem(variable.Item),
                    BoolValue = variable.Value is bool variableBool ? variableBool : null,
                    StringValue = variable.Value as string
                };

            case InsertNodeAction insert:
                return SerializeNodeAction(InsertNode, insert.Description, insert.Parent, insert.Node, insert.Index);

            case RemoveNodeAction remove:
                return SerializeNodeAction(RemoveNode, remove.Description, remove.Parent, remove.Node, remove.Index);

            case InsertKeyValueRowAction insertRow:
                return new DirtyUndoActionDto
                {
                    Type = InsertKeyValueRow,
                    Description = insertRow.Description,
                    RequestId = TryGetId(insertRow.Request),
                    ListKind = insertRow.ListKind.ToString(),
                    Index = insertRow.Index,
                    ItemId = GetItemId(insertRow.Item, items),
                    Item = SnapshotItem(insertRow.Item)
                };

            case RemoveKeyValueRowAction removeRow:
                return new DirtyUndoActionDto
                {
                    Type = RemoveKeyValueRow,
                    Description = removeRow.Description,
                    RequestId = TryGetId(removeRow.Request),
                    ListKind = removeRow.ListKind.ToString(),
                    Index = removeRow.Index,
                    ItemId = GetItemId(removeRow.Item, items),
                    ItemIndex = removeRow.Request.IndexOfKeyValue(removeRow.ListKind, removeRow.Item),
                    Item = SnapshotItem(removeRow.Item)
                };

            case InsertVariableRowAction insertVar:
                return new DirtyUndoActionDto
                {
                    Type = InsertVariableRow,
                    Description = insertVar.Description,
                    ContainerId = TryGetId(insertVar.Container),
                    Index = insertVar.Index,
                    ItemId = GetItemId(insertVar.Item, items),
                    Item = SnapshotItem(insertVar.Item)
                };

            case RemoveVariableRowAction removeVar:
                return new DirtyUndoActionDto
                {
                    Type = RemoveVariableRow,
                    Description = removeVar.Description,
                    ContainerId = TryGetId(removeVar.Container),
                    Index = removeVar.Index,
                    ItemId = GetItemId(removeVar.Item, items),
                    ItemIndex = removeVar.Container.IndexOfVariable(removeVar.Item),
                    Item = SnapshotItem(removeVar.Item)
                };

            default:
                return null;
        }
    }

    private static DirtyUndoActionDto SerializeNodeAction(
        string type,
        string description,
        TreeNodeViewModel? parent,
        TreeNodeViewModel node,
        int index)
    {
        SyncNodeModel(node);
        return new DirtyUndoActionDto
        {
            Type = type,
            Description = description,
            ParentId = parent is null ? null : TryGetId(parent),
            NodeId = TryGetId(node),
            Index = index,
            NodeKind = NodeKind(node),
            NodeModel = JsonSerializer.SerializeToElement(NodeModel(node), JsonOptions)
        };
    }

    private static bool TryCreateAction(
        DirtyUndoActionDto dto,
        DirtyUndoBindContext context,
        out IUndoAction action)
    {
        action = null!;
        try
        {
            switch (dto.Type)
            {
                case Composite:
                    if (dto.Steps is null)
                    {
                        return false;
                    }

                    var steps = new List<IUndoAction>(dto.Steps.Count);
                    foreach (var step in dto.Steps)
                    {
                        if (!TryCreateAction(step, context, out var created))
                        {
                            return false;
                        }

                        steps.Add(created);
                    }

                    action = new CompositeUndoAction(
                        dto.Description,
                        steps,
                        ResolveNodes(dto.SelectAfter, context),
                        ResolveNodes(dto.SelectAfterInverse, context));
                    action.HistoryId = dto.HistoryId;
                    return true;

                case SetName:
                    if (context.ResolveNode(dto.NodeId) is not { } named)
                    {
                        return false;
                    }

                    action = new SetNameAction(named, dto.StringValue ?? string.Empty, dto.Description);
                    action.HistoryId = dto.HistoryId;
                    return true;

                case SetRequestBodyField:
                    if (context.ResolveNode(dto.RequestId) is not RequestNodeViewModel bodyRequest
                        || !Enum.TryParse<RequestBodyField>(dto.Field, ignoreCase: true, out var bodyField))
                    {
                        return false;
                    }

                    action = new SetRequestBodyFieldAction(bodyRequest, bodyField, dto.StringValue, dto.Description);
                    action.HistoryId = dto.HistoryId;
                    return true;

                case SetRequestAddress:
                    if (context.ResolveNode(dto.RequestId) is not RequestNodeViewModel addressRequest)
                    {
                        return false;
                    }

                    action = new SetRequestAddressAction(
                        addressRequest,
                        dto.StringValue ?? string.Empty,
                        dto.Description);
                    action.HistoryId = dto.HistoryId;
                    return true;

                case SetKeyValueField:
                    if (context.ResolveNode(dto.RequestId) is not RequestNodeViewModel kvRequest
                        || !Enum.TryParse<KeyValueListKind>(dto.ListKind, ignoreCase: true, out var kvKind)
                        || !Enum.TryParse<KeyValueField>(dto.Field, ignoreCase: true, out var kvField)
                        || context.ResolveListItem(kvRequest, kvKind, dto) is not { } kvItem)
                    {
                        return false;
                    }

                    action = new SetKeyValueFieldAction(
                        kvRequest,
                        kvKind,
                        kvItem,
                        kvField,
                        FieldValue(dto),
                        dto.Description);
                    action.HistoryId = dto.HistoryId;
                    return true;

                case SetVariableField:
                    if (context.ResolveNode(dto.ContainerId) is not ContainerNodeViewModel variableContainer
                        || !Enum.TryParse<KeyValueField>(dto.Field, ignoreCase: true, out var variableField)
                        || context.ResolveVariableItem(variableContainer, dto) is not { } variableItem)
                    {
                        return false;
                    }

                    action = new SetVariableFieldAction(
                        variableContainer,
                        variableItem,
                        variableField,
                        FieldValue(dto),
                        dto.Description);
                    action.HistoryId = dto.HistoryId;
                    return true;

                case InsertNode:
                    if (!TryResolveNodePair(dto, context, out var insertParent, out var insertNode))
                    {
                        return false;
                    }

                    action = new InsertNodeAction(
                        insertParent,
                        insertNode,
                        dto.Index ?? 0,
                        dto.Description);
                    action.HistoryId = dto.HistoryId;
                    return true;

                case RemoveNode:
                    if (!TryResolveNodePair(dto, context, out var removeParent, out var removeNode))
                    {
                        return false;
                    }

                    action = new RemoveNodeAction(
                        removeNode,
                        removeParent,
                        dto.Index ?? 0,
                        dto.Description);
                    action.HistoryId = dto.HistoryId;
                    return true;

                case InsertKeyValueRow:
                    if (context.ResolveNode(dto.RequestId) is not RequestNodeViewModel insertRowRequest
                        || !Enum.TryParse<KeyValueListKind>(dto.ListKind, ignoreCase: true, out var insertKind)
                        || context.ResolveOrCreateListItem(insertRowRequest, insertKind, dto) is not { } insertRowItem)
                    {
                        return false;
                    }

                    action = new InsertKeyValueRowAction(
                        insertRowRequest,
                        insertKind,
                        insertRowItem,
                        dto.Index ?? 0,
                        dto.Description);
                    action.HistoryId = dto.HistoryId;
                    return true;

                case RemoveKeyValueRow:
                    if (context.ResolveNode(dto.RequestId) is not RequestNodeViewModel removeRowRequest
                        || !Enum.TryParse<KeyValueListKind>(dto.ListKind, ignoreCase: true, out var removeKind)
                        || context.ResolveListItem(removeRowRequest, removeKind, dto) is not { } removeRowItem)
                    {
                        return false;
                    }

                    action = new RemoveKeyValueRowAction(
                        removeRowRequest,
                        removeKind,
                        removeRowItem,
                        dto.Index ?? 0,
                        dto.Description);
                    action.HistoryId = dto.HistoryId;
                    return true;

                case InsertVariableRow:
                    if (context.ResolveNode(dto.ContainerId) is not ContainerNodeViewModel insertVarContainer
                        || context.ResolveOrCreateVariableItem(insertVarContainer, dto) is not { } insertVarItem)
                    {
                        return false;
                    }

                    action = new InsertVariableRowAction(
                        insertVarContainer,
                        insertVarItem,
                        dto.Index ?? 0,
                        dto.Description);
                    action.HistoryId = dto.HistoryId;
                    return true;

                case RemoveVariableRow:
                    if (context.ResolveNode(dto.ContainerId) is not ContainerNodeViewModel removeVarContainer
                        || context.ResolveVariableItem(removeVarContainer, dto) is not { } removeVarItem)
                    {
                        return false;
                    }

                    action = new RemoveVariableRowAction(
                        removeVarContainer,
                        removeVarItem,
                        dto.Index ?? 0,
                        dto.Description);
                    action.HistoryId = dto.HistoryId;
                    return true;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveNodePair(
        DirtyUndoActionDto dto,
        DirtyUndoBindContext context,
        out TreeNodeViewModel? parent,
        out TreeNodeViewModel node)
    {
        parent = dto.ParentId is Guid parentId ? context.ResolveNode(parentId) : null;
        node = context.ResolveNode(dto.NodeId, dto.NodeKind, dto.NodeModel)!;
        if (node is null)
        {
            return false;
        }

        if (dto.ParentId is not null && parent is null && node is not CollectionNodeViewModel)
        {
            return false;
        }

        return true;
    }

    private static List<TreeNodeViewModel>? ResolveNodes(List<Guid>? ids, DirtyUndoBindContext context)
    {
        if (ids is null)
        {
            return null;
        }

        var nodes = new List<TreeNodeViewModel>();
        foreach (var id in ids)
        {
            if (context.ResolveNode(id) is { } node)
            {
                nodes.Add(node);
            }
        }

        return nodes;
    }

    private static object? FieldValue(DirtyUndoActionDto dto) =>
        dto.BoolValue.HasValue ? dto.BoolValue.Value : dto.StringValue;

    private static Guid GetItemId(
        KeyValueItemViewModel item,
        Dictionary<KeyValueItemViewModel, Guid> items)
    {
        if (!items.TryGetValue(item, out var id))
        {
            id = Guid.NewGuid();
            items[item] = id;
        }

        return id;
    }

    private static KeyValueItemSnapshot SnapshotItem(KeyValueItemViewModel item) =>
        new()
        {
            IsEnabled = item.IsEnabled,
            Key = item.Key ?? string.Empty,
            Value = item.Value ?? string.Empty
        };

    private static List<Guid>? ToNodeIds(IReadOnlyList<TreeNodeViewModel>? nodes)
    {
        if (nodes is null)
        {
            return null;
        }

        var ids = new List<Guid>(nodes.Count);
        foreach (var node in nodes)
        {
            var id = TryGetId(node);
            if (id is Guid guid)
            {
                ids.Add(guid);
            }
        }

        return ids;
    }

    private static Guid? TryGetId(TreeNodeViewModel node) =>
        node switch
        {
            CollectionNodeViewModel collection => collection.Model.Id,
            FolderNodeViewModel folder => folder.Model.Id,
            RequestNodeViewModel request => request.Model.Id,
            _ => null
        };

    private static string NodeKind(TreeNodeViewModel node) =>
        node switch
        {
            CollectionNodeViewModel => "collection",
            FolderNodeViewModel => "folder",
            RequestNodeViewModel => "request",
            _ => "unknown"
        };

    private static object NodeModel(TreeNodeViewModel node) =>
        node switch
        {
            CollectionNodeViewModel collection => collection.Model,
            FolderNodeViewModel folder => folder.Model,
            RequestNodeViewModel request => request.Model,
            _ => throw new InvalidOperationException("Unsupported node type.")
        };

    private static void SyncNodeModel(TreeNodeViewModel node)
    {
        switch (node)
        {
            case CollectionNodeViewModel collection:
                CollectionTreeMapper.SyncModelFromView(collection);
                break;
            case FolderNodeViewModel folder:
                CollectionTreeMapper.SyncFolder(folder);
                break;
        }
    }
}
