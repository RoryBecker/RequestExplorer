using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class SetRequestDescriptionAction : IUndoAction, ICoalescingUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly string _value;

    public SetRequestDescriptionAction(
        RequestNodeViewModel request,
        string value,
        string description)
    {
        _request = request;
        _value = value;
        Description = description;
    }

    public RequestNodeViewModel Request => _request;

    public string Value => _value;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_request];

    public UndoRevealTarget? RevealTarget => new(_request, EditorSurface.Description);

    public bool CanCoalesceWith(IUndoAction incoming) =>
        incoming is SetRequestDescriptionAction other
        && ReferenceEquals(_request, other._request);

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _request.Description;
        _request.ApplyDescription(_value);
        context.Select(_request);
        return new SetRequestDescriptionAction(_request, previous, Description);
    }
}
