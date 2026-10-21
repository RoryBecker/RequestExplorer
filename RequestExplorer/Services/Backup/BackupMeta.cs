namespace RequestExplorer.Services.Backup;

public sealed class BackupMeta
{
    public DateTimeOffset CreatedUtc { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string AppVersion { get; set; } = string.Empty;

    public int CollectionCount { get; set; }
}
