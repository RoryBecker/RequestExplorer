using RequestExplorer.ViewModels;

namespace RequestExplorer.Services.Undo;

public sealed class SetRequestAddressAction : IUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly string _address;

    public SetRequestAddressAction(RequestNodeViewModel request, string address, string description)
    {
        _request = request;
        _address = address;
        Description = description;
    }

    public string Description { get; }

    public UndoRevealTarget? RevealTarget => new(_request, EditorSurface.None);

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _request.Address ?? string.Empty;
        _request.ApplyAddress(_address);
        context.Select(_request);
        return new SetRequestAddressAction(_request, previous, Description);
    }
}
