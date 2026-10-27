using System.Text.Json;

namespace RequestExplorer.Services.Undo;

public sealed class DirtyUndoStackSnapshot
{
    public List<DirtyUndoActionDto> Undo { get; set; } = [];

    public List<DirtyUndoActionDto> Redo { get; set; } = [];

    public long NextHistoryId { get; set; }
}

public sealed class DirtyUndoActionDto
{
    public string Type { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public long HistoryId { get; set; }

    public Guid? NodeId { get; set; }

    public Guid? ParentId { get; set; }

    public Guid? RequestId { get; set; }

    public Guid? ContainerId { get; set; }

    public Guid? ItemId { get; set; }

    public int? Index { get; set; }

    public int? ItemIndex { get; set; }

    public string? StringValue { get; set; }

    public bool? BoolValue { get; set; }

    public string? Field { get; set; }

    public string? ListKind { get; set; }

    public string? NodeKind { get; set; }

    public JsonElement? NodeModel { get; set; }

    public KeyValueItemSnapshot? Item { get; set; }

    public List<DirtyUndoActionDto>? Steps { get; set; }

    public List<Guid>? SelectAfter { get; set; }

    public List<Guid>? SelectAfterInverse { get; set; }

    public List<Guid>? ItemIds { get; set; }

    public List<int>? ItemIndices { get; set; }
}

public sealed class KeyValueItemSnapshot
{
    public bool IsEnabled { get; set; } = true;

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public bool IsSecret { get; set; }
}
