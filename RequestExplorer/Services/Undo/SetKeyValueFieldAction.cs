using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class SetKeyValueFieldAction : IUndoAction, ICoalescingUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly KeyValueListKind _listKind;
    private readonly KeyValueItemViewModel _item;
    private readonly KeyValueField _field;
    private readonly object? _value;

    public SetKeyValueFieldAction(
        RequestNodeViewModel request,
        KeyValueListKind listKind,
        KeyValueItemViewModel item,
        KeyValueField field,
        object? value,
        string description)
    {
        _request = request;
        _listKind = listKind;
        _item = item;
        _field = field;
        _value = value;
        Description = description;
    }

    public string Description { get; }

    public UndoRevealTarget? RevealTarget =>
        new(_request, _listKind == KeyValueListKind.Params ? EditorSurface.Params : EditorSurface.Headers);

    public bool CanCoalesceWith(IUndoAction incoming) =>
        incoming is SetKeyValueFieldAction other
        && ReferenceEquals(_request, other._request)
        && _listKind == other._listKind
        && ReferenceEquals(_item, other._item)
        && _field == other._field
        && _field is not KeyValueField.IsEnabled;

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _item.GetField(_field);
        _request.ApplyKeyValueField(_listKind, _item, _field, _value);
        context.Select(_request);
        return new SetKeyValueFieldAction(_request, _listKind, _item, _field, previous, Description);
    }
}
