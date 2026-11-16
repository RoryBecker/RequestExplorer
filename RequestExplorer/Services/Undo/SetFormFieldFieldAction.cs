using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class SetFormFieldFieldAction : IUndoAction, ICoalescingUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly FormFieldItemViewModel _item;
    private readonly FormFieldField _field;
    private readonly object? _value;

    public SetFormFieldFieldAction(
        RequestNodeViewModel request,
        FormFieldItemViewModel item,
        FormFieldField field,
        object? value,
        string description)
    {
        _request = request;
        _item = item;
        _field = field;
        _value = value;
        Description = description;
    }

    public RequestNodeViewModel Request => _request;

    public FormFieldItemViewModel Item => _item;

    public FormFieldField Field => _field;

    public object? Value => _value;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_request];

    public UndoRevealTarget? RevealTarget => new(_request, EditorSurface.Body);

    public bool CanCoalesceWith(IUndoAction incoming) =>
        incoming is SetFormFieldFieldAction other
        && ReferenceEquals(_request, other._request)
        && ReferenceEquals(_item, other._item)
        && _field == other._field
        && _field is not FormFieldField.IsEnabled and not FormFieldField.Type;

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _item.GetField(_field);
        _request.ApplyFormField(_item, _field, _value);
        context.Select(_request);
        return new SetFormFieldFieldAction(_request, _item, _field, previous, Description);
    }
}
