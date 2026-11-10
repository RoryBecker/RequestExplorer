using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class RemoveHeaderRowAction : IUndoAction
{
    private readonly ContainerNodeViewModel _container;
    private readonly KeyValueItemViewModel _item;
    private readonly int _index;

    public RemoveHeaderRowAction(
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

    public UndoRevealTarget? RevealTarget => new(_container, EditorSurface.ContainerHeaders);

    public IUndoAction Execute(IUndoContext context)
    {
        var index = _container.IndexOfHeader(_item);
        if (index < 0)
        {
            index = _index;
        }

        _container.RemoveHeaderRow(_item);
        context.Select(_container);
        return new InsertHeaderRowAction(_container, _item, index, Description);
    }
}
