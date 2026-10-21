using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public readonly record struct UndoRevealTarget(TreeNodeViewModel Node, EditorSurface Surface);
