using RequestExplorer.ViewModels;

namespace RequestExplorer.Services.Undo;

public sealed class RemoveVariableRowAction : IUndoAction
{
    private readonly ContainerNodeViewModel _container;
    private readonly KeyValueItemViewModel _item;
    private readonly int _index;

    public RemoveVariableRowAction(
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

    public string Description { get; }

    public UndoRevealTarget? RevealTarget => new(_container, EditorSurface.Variables);

    public IUndoAction Execute(IUndoContext context)
    {
        _container.RemoveVariableRow(_item);
        context.Select(_container);
        return new InsertVariableRowAction(_container, _item, _index, Description);
    }
}
