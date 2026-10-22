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
}
