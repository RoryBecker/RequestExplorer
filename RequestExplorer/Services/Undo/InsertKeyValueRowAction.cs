using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class InsertKeyValueRowAction : IUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly KeyValueListKind _listKind;
    private readonly KeyValueItemViewModel _item;
    private readonly int _index;

    public InsertKeyValueRowAction(
        RequestNodeViewModel request,
        KeyValueListKind listKind,
        KeyValueItemViewModel item,
        int index,
        string description)
    {
        _request = request;
        _listKind = listKind;
        _item = item;
        _index = index;
        Description = description;
    }

    public RequestNodeViewModel Request => _request;

    public KeyValueListKind ListKind => _listKind;

    public KeyValueItemViewModel Item => _item;

    public int Index => _index;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_request];

    public UndoRevealTarget? RevealTarget =>
        new(_request, _listKind == KeyValueListKind.Params ? EditorSurface.Params : EditorSurface.Headers);

    public IUndoAction Execute(IUndoContext context)
    {
        _request.InsertKeyValueRow(_listKind, _item, _index);
        context.Select(_request);
        var index = _request.IndexOfKeyValue(_listKind, _item);
        return new RemoveKeyValueRowAction(_request, _listKind, _item, index, Description);
    }
}
