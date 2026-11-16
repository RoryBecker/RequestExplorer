using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class InsertFormFieldRowAction : IUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly FormFieldItemViewModel _item;
    private readonly int _index;

    public InsertFormFieldRowAction(
        RequestNodeViewModel request,
        FormFieldItemViewModel item,
        int index,
        string description)
    {
        _request = request;
        _item = item;
        _index = index;
        Description = description;
    }

    public RequestNodeViewModel Request => _request;

    public FormFieldItemViewModel Item => _item;

    public int Index => _index;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_request];

    public UndoRevealTarget? RevealTarget => new(_request, EditorSurface.Body);

    public IUndoAction Execute(IUndoContext context)
    {
        _request.InsertFormFieldRow(_item, _index);
        context.Select(_request);
        return new RemoveFormFieldRowAction(_request, _item, $"Remove form field");
    }
}
