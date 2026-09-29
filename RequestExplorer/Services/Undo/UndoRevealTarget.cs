using RequestExplorer.ViewModels;

namespace RequestExplorer.Services.Undo;

public enum EditorSurface
{
    None,
    Params,
    Body,
    Headers,
    Auth,
    Variables
}

public readonly record struct UndoRevealTarget(TreeNodeViewModel Node, EditorSurface Surface);
