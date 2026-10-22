using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class RemoveNodeAction : IUndoAction
{
    private readonly TreeNodeViewModel _node;
    private readonly TreeNodeViewModel? _parent;
    private readonly int _index;

    public RemoveNodeAction(TreeNodeViewModel node, TreeNodeViewModel? parent, int index, string description)
    {
        _node = node;
        _parent = parent;
        _index = index;
        Description = description;
    }

    public TreeNodeViewModel Node => _node;

    public TreeNodeViewModel? Parent => _parent;

    public int Index => _index;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() =>
        UndoAffectedNodes.ForTreeChange(_node, _parent);

    public UndoRevealTarget? RevealTarget => null;

    public IUndoAction Execute(IUndoContext context)
    {
        context.RemoveNode(_node);
        context.Select(_parent);
        return new InsertNodeAction(_parent, _node, _index, Description);
    }
}
