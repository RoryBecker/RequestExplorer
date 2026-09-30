using RequestExplorer.ViewModels;

namespace RequestExplorer.Services;

/// <summary>Drag payload for one or more tree nodes moved together.</summary>
public sealed class TreeNodeDragPayload
{
    public TreeNodeDragPayload(IReadOnlyList<TreeNodeViewModel> nodes, TreeNodeViewModel primary)
    {
        Nodes = nodes.Count > 0 ? nodes : [primary];
        Primary = primary;
    }

    public IReadOnlyList<TreeNodeViewModel> Nodes { get; }

    public TreeNodeViewModel Primary { get; }
}
