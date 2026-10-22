using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class SetRequestBodyFieldAction : IUndoAction, ICoalescingUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly RequestBodyField _field;
    private readonly string? _value;

    public SetRequestBodyFieldAction(
        RequestNodeViewModel request,
        RequestBodyField field,
        string? value,
        string description)
    {
        _request = request;
        _field = field;
        _value = value;
        Description = description;
    }

    public RequestNodeViewModel Request => _request;

    public RequestBodyField Field => _field;

    public string? Value => _value;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_request];

    public UndoRevealTarget? RevealTarget => new(_request, EditorSurface.Body);

    public bool CanCoalesceWith(IUndoAction incoming) =>
        incoming is SetRequestBodyFieldAction other
        && ReferenceEquals(_request, other._request)
        && _field == other._field;

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _request.GetBodyField(_field);
        _request.ApplyBodyField(_field, _value);
        context.Select(_request);
        return new SetRequestBodyFieldAction(_request, _field, previous, Description);
    }
}
