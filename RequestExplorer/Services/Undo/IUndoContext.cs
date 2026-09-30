using RequestExplorer.ViewModels;

namespace RequestExplorer.Services.Undo;

public interface IUndoContext
{
    void SetName(TreeNodeViewModel node, string name);

    void RemoveNode(TreeNodeViewModel node);

    void InsertNode(TreeNodeViewModel? parent, TreeNodeViewModel node, int index);

    void Select(TreeNodeViewModel? node);

    /// <summary>
    /// Expands ancestors, clears the tree filter if needed, selects the nodes,
    /// and requests that they be scrolled into view.
    /// </summary>
    void RevealAndSelect(IReadOnlyList<TreeNodeViewModel> nodes);
}
