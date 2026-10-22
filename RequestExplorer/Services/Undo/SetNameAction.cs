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

    public TreeNodeViewModel Node => _node;

    public string Name => _name;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_node];

    public UndoRevealTarget? RevealTarget => null;

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _node.Name;
        context.SetName(_node, _name);
        context.Select(_node);
        return new SetNameAction(_node, previous, Description);
    }
}
