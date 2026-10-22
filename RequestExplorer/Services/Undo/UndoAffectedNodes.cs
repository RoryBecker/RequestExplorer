using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

internal static class UndoAffectedNodes
{
    public static IEnumerable<TreeNodeViewModel> ForTreeChange(
        TreeNodeViewModel node,
        TreeNodeViewModel? parent)
    {
        yield return node;
        if (parent is not null)
        {
            yield return parent;
        }

        foreach (var descendant in EnumerateDescendants(node))
        {
            yield return descendant;
        }
    }

    private static IEnumerable<TreeNodeViewModel> EnumerateDescendants(TreeNodeViewModel node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var descendant in EnumerateDescendants(child))
            {
                yield return descendant;
            }
        }
    }
}
