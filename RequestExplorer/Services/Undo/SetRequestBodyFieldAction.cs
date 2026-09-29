using RequestExplorer.ViewModels;

namespace RequestExplorer.Services.Undo;

public enum RequestBodyField
{
    Content,
    ContentType
}

public sealed class SetRequestBodyFieldAction : IUndoAction
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

    public string Description { get; }

    public UndoRevealTarget? RevealTarget => new(_request, EditorSurface.Body);

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _request.GetBodyField(_field);
        _request.ApplyBodyField(_field, _value);
        context.Select(_request);
        return new SetRequestBodyFieldAction(_request, _field, previous, Description);
    }
}
