namespace RequestExplorer.Services.Backup;

public sealed class BackupListItem
{
    public required string ZipPath { get; init; }

    public required string DisplayName { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string AppVersion { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public int CollectionCount { get; init; }

    public string SizeDisplay =>
        SizeBytes < 1024
            ? $"{SizeBytes} B"
            : SizeBytes < 1024 * 1024
                ? $"{SizeBytes / 1024.0:0.#} KB"
                : $"{SizeBytes / (1024.0 * 1024.0):0.#} MB";

    public string ReasonDisplay => Reason switch
    {
        "startup" => "Startup",
        "manual" => "Manual",
        "pre-restore" => "Pre-restore",
        _ => string.IsNullOrWhiteSpace(Reason) ? "—" : Reason
    };
}
