using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class InsertVariableRowAction : IUndoAction
{
    private readonly ContainerNodeViewModel _container;
    private readonly KeyValueItemViewModel _item;
    private readonly int _index;

    public InsertVariableRowAction(
        ContainerNodeViewModel container,
        KeyValueItemViewModel item,
        int index,
        string description)
    {
        _container = container;
        _item = item;
        _index = index;
        Description = description;
    }

    public ContainerNodeViewModel Container => _container;

    public KeyValueItemViewModel Item => _item;

    public int Index => _index;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_container];

    public UndoRevealTarget? RevealTarget => new(_container, EditorSurface.Variables);

    public IUndoAction Execute(IUndoContext context)
    {
        _container.InsertVariableRow(_item, _index);
        context.Select(_container);
        var index = _container.IndexOfVariable(_item);
        return new RemoveVariableRowAction(_container, _item, index, Description);
    }
}
