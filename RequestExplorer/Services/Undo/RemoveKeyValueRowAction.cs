using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class RemoveKeyValueRowAction : IUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly KeyValueListKind _listKind;
    private readonly KeyValueItemViewModel _item;
    private readonly int _index;

    public RemoveKeyValueRowAction(
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
        _request.RemoveKeyValueRow(_listKind, _item);
        context.Select(_request);
        return new InsertKeyValueRowAction(_request, _listKind, _item, _index, Description);
    }
}
