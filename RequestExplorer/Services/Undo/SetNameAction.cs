using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class SetNameAction : IUndoAction
{
    private readonly TreeNodeViewModel _node;
    private readonly string _name;

    public SetNameAction(TreeNodeViewModel node, string name, string description)
    {
        _node = node;
        _name = name;
        Description = description;
    }

    public string Description { get; }

    public UndoRevealTarget? RevealTarget => null;

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _node.Name;
        context.SetName(_node, _name);
        context.Select(_node);
        return new SetNameAction(_node, previous, Description);
    }
}
