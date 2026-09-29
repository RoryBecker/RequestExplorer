using RequestExplorer.ViewModels;

namespace RequestExplorer.Services.Undo;

public enum EditorSurface
{
    None,
    Params,
    Body,
    Headers,
    Auth
}

public readonly record struct UndoRevealTarget(RequestNodeViewModel Request, EditorSurface Surface);
