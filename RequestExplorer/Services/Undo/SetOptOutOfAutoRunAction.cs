using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class SetOptOutOfAutoRunAction : IUndoAction
{
    private readonly TreeNodeViewModel _node;
    private readonly bool _value;

    public SetOptOutOfAutoRunAction(TreeNodeViewModel node, bool value, string description)
    {
        _node = node;
        _value = value;
        Description = description;
    }

    public TreeNodeViewModel Node => _node;

    public bool Value => _value;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_node];

    public UndoRevealTarget? RevealTarget => new(_node, EditorSurface.None);

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = Get(_node);
        Apply(_node, _value);
        context.Select(_node);
        return new SetOptOutOfAutoRunAction(_node, previous, Description);
    }

    public static bool Get(TreeNodeViewModel node) =>
        node switch
        {
            CollectionNodeViewModel collection => collection.OptOutOfAutoRun,
            FolderNodeViewModel folder => folder.OptOutOfAutoRun,
            RequestNodeViewModel request => request.OptOutOfAutoRun,
            _ => false
        };

    public static void Apply(TreeNodeViewModel node, bool value)
    {
        switch (node)
        {
            case CollectionNodeViewModel collection:
                collection.ApplyOptOutOfAutoRun(value);
                break;
            case FolderNodeViewModel folder:
                folder.ApplyOptOutOfAutoRun(value);
                break;
            case RequestNodeViewModel request:
                request.ApplyOptOutOfAutoRun(value);
                break;
        }
    }
}
