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
}
