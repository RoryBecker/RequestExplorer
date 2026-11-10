namespace RequestExplorer.Services.Undo;

public enum EditorSurface
{
    None,
    Description,
    Params,
    Body,
    Headers,
    Auth,
    Variables,
    /// <summary>Headers tab on a collection or folder editor.</summary>
    ContainerHeaders
}
