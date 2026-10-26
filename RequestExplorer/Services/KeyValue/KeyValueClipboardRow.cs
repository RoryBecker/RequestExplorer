namespace RequestExplorer.Services.KeyValue;

public sealed class KeyValueClipboardRow
{
    public bool IsEnabled { get; set; } = true;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}
