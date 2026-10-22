using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

/// <summary>
/// An action that can be executed and returns the inverse action for the opposite stack.
/// </summary>
public interface IUndoAction
{
    string Description { get; }

    /// <summary>
    /// Stable id for this edit on the undo/redo timeline. Copied onto the inverse
    /// so undo then redo refers to the same checkpoint.
    /// </summary>
    long HistoryId { get; set; }

    /// <summary>Nodes whose dirty mark this action can change relative to disk.</summary>
    IEnumerable<TreeNodeViewModel> GetAffectedNodes();

    /// <summary>
    /// When set, Undo/Redo first reveals this surface if it is not already visible;
    /// a subsequent Undo/Redo then executes the action.
    /// </summary>
    UndoRevealTarget? RevealTarget { get; }

    /// <summary>Applies this action and returns the inverse instruction.</summary>
    IUndoAction Execute(IUndoContext context);
}
