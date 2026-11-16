using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class RemoveFormFieldRowAction : IUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly FormFieldItemViewModel _item;

    public RemoveFormFieldRowAction(
        RequestNodeViewModel request,
        FormFieldItemViewModel item,
        string description)
    {
        _request = request;
        _item = item;
        Description = description;
    }

    public RequestNodeViewModel Request => _request;

    public FormFieldItemViewModel Item => _item;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_request];

    public UndoRevealTarget? RevealTarget => new(_request, EditorSurface.Body);

    public IUndoAction Execute(IUndoContext context)
    {
        var index = _request.IndexOfFormField(_item);
        _request.RemoveFormFieldRow(_item);
        context.Select(_request);
        return new InsertFormFieldRowAction(
            _request,
            _item,
            Math.Max(0, index),
            $"Restore form field");
    }
}
