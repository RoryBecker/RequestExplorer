using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class ReorderVariableListAction : IUndoAction
{
    private readonly ContainerNodeViewModel _container;
    private readonly KeyValueItemViewModel[] _order;

    public ReorderVariableListAction(
        ContainerNodeViewModel container,
        IReadOnlyList<KeyValueItemViewModel> order,
        string description)
    {
        _container = container;
        _order = order.ToArray();
        Description = description;
    }

    public ContainerNodeViewModel Container => _container;

    public IReadOnlyList<KeyValueItemViewModel> Order => _order;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_container];

    public UndoRevealTarget? RevealTarget => new(_container, EditorSurface.Variables);

    public IUndoAction Execute(IUndoContext context)
    {
        var list = _container.Variables;
        var inverse = list.ToArray();
        KeyValueListReorder.ApplyOrder(list, _order);
        _container.OnKeyValueListReordered(list);
        context.Select(_container);
        return new ReorderVariableListAction(_container, inverse, Description);
    }
}
