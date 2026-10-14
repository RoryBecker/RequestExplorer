using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class InsertNodeAction : IUndoAction
{
    private readonly TreeNodeViewModel? _parent;
    private readonly TreeNodeViewModel _node;
    private readonly int _index;

    public InsertNodeAction(TreeNodeViewModel? parent, TreeNodeViewModel node, int index, string description)
    {
        _parent = parent;
        _node = node;
        _index = index;
        Description = description;
    }

    public string Description { get; }

    public UndoRevealTarget? RevealTarget => null;

    public IUndoAction Execute(IUndoContext context)
    {
        context.InsertNode(_parent, _node, _index);
        context.Select(_node);
        // Capture current location after insert for a precise inverse remove.
        var parent = _node.Parent;
        var index = parent is not null
            ? parent.Children.IndexOf(_node)
            : _index;
        return new RemoveNodeAction(_node, parent, index, Description);
    }
}
