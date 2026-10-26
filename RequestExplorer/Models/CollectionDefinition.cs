using System.Text.Json.Serialization;

namespace RequestExplorer.Models;

public sealed class CollectionDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Collection";

    /// <summary>Optional notes shown in the collection editor.</summary>
    public string? Description { get; set; }

    /// <summary>When true, descendant requests are skipped by Run All (still diagnosed).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OptOutOfAutoRun { get; set; }

    public List<KeyValueItem> Variables { get; set; } = [];
    public List<FolderDefinition> Folders { get; set; } = [];
    public List<RequestDefinition> Requests { get; set; } = [];

    /// <summary>Interleaved sibling order; empty means legacy folders-then-requests.</summary>
    public List<ChildOrderEntry> ChildOrder { get; set; } = [];
}
