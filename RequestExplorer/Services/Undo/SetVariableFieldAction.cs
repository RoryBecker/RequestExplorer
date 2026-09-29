using RequestExplorer.ViewModels;

namespace RequestExplorer.Services.Undo;

public sealed class SetVariableFieldAction : IUndoAction
{
    private readonly ContainerNodeViewModel _container;
    private readonly KeyValueItemViewModel _item;
    private readonly KeyValueField _field;
    private readonly object? _value;

    public SetVariableFieldAction(
        ContainerNodeViewModel container,
        KeyValueItemViewModel item,
        KeyValueField field,
        object? value,
        string description)
    {
        _container = container;
        _item = item;
        _field = field;
        _value = value;
        Description = description;
    }

    public string Description { get; }

    public UndoRevealTarget? RevealTarget => new(_container, EditorSurface.Variables);

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _item.GetField(_field);
        _container.ApplyVariableField(_item, _field, _value);
        context.Select(_container);
        return new SetVariableFieldAction(_container, _item, _field, previous, Description);
    }
}
