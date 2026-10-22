using RequestExplorer.Models;

namespace RequestExplorer.Services.Persistence;

/// <summary>
/// One collection's unsaved document plus the node ids that were dirty
/// when the snapshot was written.
/// </summary>
public sealed class DirtyCollectionSnapshot
{
    public CollectionDefinition Collection { get; set; } = new();

    public List<Guid> DirtyNodeIds { get; set; } = [];

    /// <summary>
    /// Undo-stack history id that matches this collection's last on-disk save.
    /// 0 means the origin (loaded/empty stack).
    /// </summary>
    public long DiskHistoryId { get; set; }

    public List<Guid> DivergedNodeIds { get; set; } = [];
}
