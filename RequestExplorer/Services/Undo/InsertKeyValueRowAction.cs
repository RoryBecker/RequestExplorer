using RequestExplorer.ViewModels;

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

    public string Description { get; }

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
