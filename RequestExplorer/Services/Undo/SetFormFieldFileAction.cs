using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

/// <summary>Undoable file attach: restores previous file name + base64 together.</summary>
public sealed class SetFormFieldFileAction : IUndoAction
{
    private readonly RequestNodeViewModel _request;
    private readonly FormFieldItemViewModel _item;
    private readonly string _fileName;
    private readonly string _base64Content;

    public SetFormFieldFileAction(
        RequestNodeViewModel request,
        FormFieldItemViewModel item,
        string fileName,
        string base64Content,
        string description)
    {
        _request = request;
        _item = item;
        _fileName = fileName ?? string.Empty;
        _base64Content = base64Content ?? string.Empty;
        Description = description;
    }

    public RequestNodeViewModel Request => _request;

    public FormFieldItemViewModel Item => _item;

    public string FileName => _fileName;

    public string Base64Content => _base64Content;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_request];

    public UndoRevealTarget? RevealTarget => new(_request, EditorSurface.Body);

    public IUndoAction Execute(IUndoContext context)
    {
        var previousName = _item.FileName ?? string.Empty;
        var previousBytes = _item.Base64Content ?? string.Empty;
        _request.ApplyFormFieldFile(_item, _fileName, _base64Content);
        context.Select(_request);
        return new SetFormFieldFileAction(_request, _item, previousName, previousBytes, Description);
    }
}
