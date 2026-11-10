using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class ReorderHeaderListAction : IUndoAction
{
    private readonly ContainerNodeViewModel _container;
    private readonly KeyValueItemViewModel[] _order;

    public ReorderHeaderListAction(
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

    public UndoRevealTarget? RevealTarget => new(_container, EditorSurface.ContainerHeaders);

    public IUndoAction Execute(IUndoContext context)
    {
        var list = _container.Headers;
        var inverse = list.ToArray();
        KeyValueListReorder.ApplyOrder(list, _order);
        _container.OnKeyValueListReordered(list);
        context.Select(_container);
        return new ReorderHeaderListAction(_container, inverse, Description);
    }
}
