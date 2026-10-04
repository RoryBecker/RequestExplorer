namespace RequestExplorer.Models;

/// <summary>Preserves interleaved folder/request sibling order in persisted collections.</summary>
public enum TreeChildKind
{
    Folder,
    Request
}

public sealed class ChildOrderEntry
{
    public TreeChildKind Kind { get; set; }

    public Guid Id { get; set; }
}
