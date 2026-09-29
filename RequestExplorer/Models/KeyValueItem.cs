namespace RequestExplorer.Models;

public sealed class KeyValueItem
{
    public bool IsEnabled { get; set; } = true;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
