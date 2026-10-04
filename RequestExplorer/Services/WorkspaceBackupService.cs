using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RequestExplorer.Services;

public enum BackupReason
{
    Startup,
    Manual,
    PreRestore
}

public sealed class BackupMeta
{
    public DateTimeOffset CreatedUtc { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string AppVersion { get; set; } = string.Empty;

    public int CollectionCount { get; set; }
}

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

/// <summary>
/// Creates and restores rolling zip backups of collections (+ prefs archived; restore is collections-only).
/// </summary>
public sealed class WorkspaceBackupService
{
    public const int DefaultRetention = 15;
    public const int MinRetention = 1;
    public const int MaxRetention = 100;

    private const string MetaFileName = "backup-meta.json";
    private const string CollectionsFolderName = "collections";
    private const string PreferencesFileName = "preferences.json";
    private const string OrderFileName = "collection-order.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();

    public WorkspaceBackupService(
        string? collectionsDirectory = null,
        string? preferencesFilePath = null,
        string? backupsDirectory = null)
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RequestExplorer");

        CollectionsDirectory = collectionsDirectory
            ?? Path.Combine(appData, CollectionsFolderName);
        PreferencesFilePath = preferencesFilePath
            ?? Path.Combine(appData, PreferencesFileName);
        BackupsDirectory = backupsDirectory
            ?? Path.Combine(appData, "backups");
    }

    public string CollectionsDirectory { get; }

    public string PreferencesFilePath { get; }

    public string BackupsDirectory { get; }

    public static string CurrentAppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    public static int ClampRetention(int value) =>
        Math.Clamp(value, MinRetention, MaxRetention);

    public void EnsureBackupsDirectory() => Directory.CreateDirectory(BackupsDirectory);

    public string CreateBackup(BackupReason reason, int retentionCount)
    {
        lock (_gate)
        {
            EnsureBackupsDirectory();
            Directory.CreateDirectory(CollectionsDirectory);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var zipName = stamp + ".zip";
            var zipPath = Path.Combine(BackupsDirectory, zipName);
            var suffix = 2;
            while (File.Exists(zipPath))
            {
                zipName = $"{stamp}-{suffix}.zip";
                zipPath = Path.Combine(BackupsDirectory, zipName);
                suffix++;
            }

            var stagingRoot = Path.Combine(
                Path.GetTempPath(),
                "RequestExplorer-backup-" + Guid.NewGuid().ToString("N"));
            var stagingCollections = Path.Combine(stagingRoot, CollectionsFolderName);
            var tempZip = Path.Combine(
                Path.GetTempPath(),
                "RequestExplorer-backup-" + Guid.NewGuid().ToString("N") + ".zip");

            try
            {
                Directory.CreateDirectory(stagingCollections);
                CopyDirectoryContents(CollectionsDirectory, stagingCollections);

                if (File.Exists(PreferencesFilePath))
                {
                    File.Copy(
                        PreferencesFilePath,
                        Path.Combine(stagingRoot, PreferencesFileName),
                        overwrite: true);
                }

                var collectionCount = CountCollectionFiles(stagingCollections);
                var meta = new BackupMeta
                {
                    CreatedUtc = DateTimeOffset.UtcNow,
                    Reason = ReasonToString(reason),
                    AppVersion = CurrentAppVersion,
                    CollectionCount = collectionCount
                };
                File.WriteAllText(
                    Path.Combine(stagingRoot, MetaFileName),
                    JsonSerializer.Serialize(meta, JsonOptions));

                if (File.Exists(tempZip))
                {
                    File.Delete(tempZip);
                }

                ZipFile.CreateFromDirectory(
                    stagingRoot,
                    tempZip,
                    CompressionLevel.Optimal,
                    includeBaseDirectory: false);

                File.Move(tempZip, zipPath);
                Prune(retentionCount);
                return zipPath;
            }
            catch
            {
                try
                {
                    if (File.Exists(tempZip))
                    {
                        File.Delete(tempZip);
                    }
                }
                catch
                {
                    // ignore
                }

                throw;
            }
            finally
            {
                TryDeleteDirectory(stagingRoot);
            }
        }
    }

    public void Prune(int retentionCount)
    {
        lock (_gate)
        {
            EnsureBackupsDirectory();
            var keep = ClampRetention(retentionCount);
            var zips = Directory.EnumerateFiles(BackupsDirectory, "*.zip")
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var obsolete in zips.Skip(keep))
            {
                try
                {
                    obsolete.Delete();
                }
                catch
                {
                    // leave locked/unreadable archives for next prune
                }
            }
        }
    }

    public IReadOnlyList<BackupListItem> ListBackups()
    {
        EnsureBackupsDirectory();
        var items = new List<BackupListItem>();

        foreach (var path in Directory.EnumerateFiles(BackupsDirectory, "*.zip")
                     .OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = new FileInfo(path);
                TryReadMeta(path, out var meta, out var countedFromZip);
                var created = meta?.CreatedUtc
                    ?? (info.CreationTimeUtc == default
                        ? new DateTimeOffset(info.LastWriteTimeUtc)
                        : new DateTimeOffset(info.CreationTimeUtc));

                items.Add(new BackupListItem
                {
                    ZipPath = path,
                    DisplayName = Path.GetFileNameWithoutExtension(path),
                    CreatedUtc = created,
                    Reason = meta?.Reason ?? string.Empty,
                    AppVersion = meta?.AppVersion ?? string.Empty,
                    SizeBytes = info.Length,
                    CollectionCount = meta is { CollectionCount: > 0 }
                        ? meta.CollectionCount
                        : countedFromZip
                });
            }
            catch
            {
                // skip unreadable zip
            }
        }

        return items;
    }

    /// <summary>
    /// Replaces the live collections directory with the collections folder from the zip.
    /// Does not modify preferences.json.
    /// </summary>
    public void RestoreCollections(string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Backup zip not found.", zipPath);
        }

        lock (_gate)
        {
            var staging = Path.Combine(
                Path.GetTempPath(),
                "RequestExplorer-restore-" + Guid.NewGuid().ToString("N"));
            var stagingCollections = Path.Combine(staging, CollectionsFolderName);
            var swapOld = CollectionsDirectory + ".pre-restore-old";

            try
            {
                Directory.CreateDirectory(staging);
                ZipFile.ExtractToDirectory(zipPath, staging);

                if (!Directory.Exists(stagingCollections))
                {
                    throw new InvalidOperationException(
                        "Backup does not contain a collections folder.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(CollectionsDirectory)!);

                if (Directory.Exists(swapOld))
                {
                    TryDeleteDirectory(swapOld);
                }

                if (Directory.Exists(CollectionsDirectory))
                {
                    Directory.Move(CollectionsDirectory, swapOld);
                }

                Directory.Move(stagingCollections, CollectionsDirectory);

                TryDeleteDirectory(swapOld);
            }
            catch
            {
                // Best-effort rollback if we moved live dir aside but failed to install new one.
                if (!Directory.Exists(CollectionsDirectory) && Directory.Exists(swapOld))
                {
                    try
                    {
                        Directory.Move(swapOld, CollectionsDirectory);
                    }
                    catch
                    {
                        // leave for manual recovery
                    }
                }

                throw;
            }
            finally
            {
                TryDeleteDirectory(staging);
                if (Directory.Exists(swapOld))
                {
                    TryDeleteDirectory(swapOld);
                }
            }
        }
    }

    private static string ReasonToString(BackupReason reason) =>
        reason switch
        {
            BackupReason.Startup => "startup",
            BackupReason.Manual => "manual",
            BackupReason.PreRestore => "pre-restore",
            _ => reason.ToString().ToLowerInvariant()
        };

    private static int CountCollectionFiles(string collectionsDir)
    {
        if (!Directory.Exists(collectionsDir))
        {
            return 0;
        }

        return Directory.EnumerateFiles(collectionsDir, "*.json")
            .Count(p => !string.Equals(Path.GetFileName(p), OrderFileName, StringComparison.OrdinalIgnoreCase));
    }

    private static void TryReadMeta(string zipPath, out BackupMeta? meta, out int collectionCountFromZip)
    {
        meta = null;
        collectionCountFromZip = 0;
        using var archive = ZipFile.OpenRead(zipPath);
        var metaEntry = archive.GetEntry(MetaFileName)
                        ?? archive.Entries.FirstOrDefault(e =>
                            string.Equals(e.Name, MetaFileName, StringComparison.OrdinalIgnoreCase));
        if (metaEntry is not null)
        {
            using var stream = metaEntry.Open();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            meta = JsonSerializer.Deserialize<BackupMeta>(json, JsonOptions);
        }

        collectionCountFromZip = archive.Entries.Count(e =>
        {
            var name = e.FullName.Replace('\\', '/');
            if (!name.StartsWith(CollectionsFolderName + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var file = Path.GetFileName(name);
            return !string.Equals(file, OrderFileName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void CopyDirectoryContents(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var dest = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            CopyDirectoryContents(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }
}
