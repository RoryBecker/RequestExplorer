using System.Text.Json.Serialization;

namespace RequestExplorer.Models;

public sealed class KeyValueItem
{
    public bool IsEnabled { get; set; } = true;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// When true, the value is omitted from Postman export. Local save keeps the value.
    /// Default false; omitted from JSON when false.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsSecret { get; set; }

    /// <summary>
    /// When false on a collection/folder definition, inheritance stops here for this name
    /// (descendants get neither this value nor a farther ancestor's). Default true.
    /// Disabled rows still fall through; this is a chain break, not a skip.
    /// </summary>
    public bool IsInheritable { get; set; } = true;
}
