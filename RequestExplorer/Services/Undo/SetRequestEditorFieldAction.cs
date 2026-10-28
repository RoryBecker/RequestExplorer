using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class SetRequestEditorFieldAction : IUndoAction, ICoalescingUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly RequestEditorField _field;
    private readonly object? _value;

    public SetRequestEditorFieldAction(
        RequestNodeViewModel request,
        RequestEditorField field,
        object? value,
        string description)
    {
        _request = request;
        _field = field;
        _value = value;
        Description = description;
    }

    public RequestNodeViewModel Request => _request;

    public RequestEditorField Field => _field;

    public object? Value => _value;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_request];

    public UndoRevealTarget? RevealTarget => new(_request, SurfaceFor(_field));

    public bool CanCoalesceWith(IUndoAction incoming) =>
        incoming is SetRequestEditorFieldAction other
        && ReferenceEquals(_request, other._request)
        && _field == other._field
        && Coalesces(_field);

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _request.GetEditorField(_field);
        _request.ApplyEditorField(_field, _value);
        context.Select(_request);
        return new SetRequestEditorFieldAction(_request, _field, previous, Description);
    }

    private static bool Coalesces(RequestEditorField field) =>
        field is RequestEditorField.BearerToken
            or RequestEditorField.BasicUsername
            or RequestEditorField.BasicPassword
            or RequestEditorField.ApiKeyName
            or RequestEditorField.ApiKeyValue
            or RequestEditorField.ExpectedBody
            or RequestEditorField.ExpectedStatusCode
            or RequestEditorField.ExpectedTimingMilliseconds;

    private static EditorSurface SurfaceFor(RequestEditorField field) =>
        field is RequestEditorField.AuthType
            or RequestEditorField.BearerToken
            or RequestEditorField.BasicUsername
            or RequestEditorField.BasicPassword
            or RequestEditorField.ApiKeyName
            or RequestEditorField.ApiKeyValue
            or RequestEditorField.ApiKeyLocation
            ? EditorSurface.Auth
            : EditorSurface.None;
}
