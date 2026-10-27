using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class ReorderKeyValueListAction : IUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly KeyValueListKind _listKind;
    private readonly KeyValueItemViewModel[] _order;

    public ReorderKeyValueListAction(
        RequestNodeViewModel request,
        KeyValueListKind listKind,
        IReadOnlyList<KeyValueItemViewModel> order,
        string description)
    {
        _request = request;
        _listKind = listKind;
        _order = order.ToArray();
        Description = description;
    }

    public RequestNodeViewModel Request => _request;

    public KeyValueListKind ListKind => _listKind;

    public IReadOnlyList<KeyValueItemViewModel> Order => _order;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_request];

    public UndoRevealTarget? RevealTarget =>
        new(_request, _listKind == KeyValueListKind.Params ? EditorSurface.Params : EditorSurface.Headers);

    public IUndoAction Execute(IUndoContext context)
    {
        var list = _listKind == KeyValueListKind.Params ? _request.Params : _request.Headers;
        var inverse = list.ToArray();
        KeyValueListReorder.ApplyOrder(list, _order);
        _request.OnKeyValueListReordered(list);
        context.Select(_request);
        return new ReorderKeyValueListAction(_request, _listKind, inverse, Description);
    }
}
