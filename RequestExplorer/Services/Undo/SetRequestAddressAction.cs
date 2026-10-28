using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class SetRequestAddressAction : IUndoAction, ICoalescingUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly string _address;

    public SetRequestAddressAction(RequestNodeViewModel request, string address, string description)
    {
        _request = request;
        _address = address;
        Description = description;
    }

    public RequestNodeViewModel Request => _request;

    public string Address => _address;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_request];

    public UndoRevealTarget? RevealTarget => new(_request, EditorSurface.None);

    public bool CanCoalesceWith(IUndoAction incoming) =>
        incoming is SetRequestAddressAction other
        && ReferenceEquals(_request, other._request)
        && string.Equals(Description, other.Description, StringComparison.Ordinal);

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _request.Address ?? string.Empty;
        _request.ApplyAddress(_address);
        context.Select(_request);
        return new SetRequestAddressAction(_request, previous, Description);
    }
}
