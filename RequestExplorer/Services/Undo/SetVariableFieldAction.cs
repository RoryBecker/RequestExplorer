using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class SetVariableFieldAction : IUndoAction, ICoalescingUndoAction
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

    public ContainerNodeViewModel Container => _container;

    public KeyValueItemViewModel Item => _item;

    public KeyValueField Field => _field;

    public object? Value => _value;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_container];

    public UndoRevealTarget? RevealTarget => new(_container, EditorSurface.Variables);

    public bool CanCoalesceWith(IUndoAction incoming) =>
        incoming is SetVariableFieldAction other
        && ReferenceEquals(_container, other._container)
        && ReferenceEquals(_item, other._item)
        && _field == other._field
        && _field is not KeyValueField.IsEnabled;

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _item.GetField(_field);
        _container.ApplyVariableField(_item, _field, _value);
        context.Select(_container);
        return new SetVariableFieldAction(_container, _item, _field, previous, Description);
    }
}
