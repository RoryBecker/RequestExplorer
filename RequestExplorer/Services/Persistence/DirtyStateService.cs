using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RequestExplorer.Models;

namespace RequestExplorer.Services.Persistence;

/// <summary>
/// Crash-recovery snapshots of unsaved collections. These files are not the
/// persisted collection documents; a real Save deletes the matching snapshot.
/// </summary>
public sealed class DirtyStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public DirtyStateService(string? dirtyDirectory = null)
    {
        DirtyDirectory = dirtyDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RequestExplorer",
                "dirty");
    }

    public string DirtyDirectory { get; }

    public void EnsureDirtyDirectory() => Directory.CreateDirectory(DirtyDirectory);

    public void Save(DirtyCollectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Collection);
        EnsureDirtyDirectory();
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        WriteTextAtomic(GetFilePath(snapshot.Collection.Id), json);
    }

    public void Delete(Guid collectionId)
    {
        var path = GetFilePath(collectionId);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to delete dirty snapshot '{path}'.",
                ex);
        }
    }

    public void DeleteAll()
    {
        if (!Directory.Exists(DirtyDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(DirtyDirectory, "*.json"))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best-effort: remaining files are retried on the next discard/save.
            }
        }
    }

    public IReadOnlyList<DirtyCollectionSnapshot> LoadAll()
    {
        if (!Directory.Exists(DirtyDirectory))
        {
            return [];
        }

        var snapshots = new List<DirtyCollectionSnapshot>();
        foreach (var path in Directory.EnumerateFiles(DirtyDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(path);
                var snapshot = DeserializeSnapshot(json);
                if (snapshot?.Collection is not null)
                {
                    snapshots.Add(snapshot);
                }
            }
            catch
            {
                // Skip unreadable snapshots so a corrupt file cannot block launch.
            }
        }

        return snapshots;
    }

    private static DirtyCollectionSnapshot? DeserializeSnapshot(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("collection", out _))
        {
            return JsonSerializer.Deserialize<DirtyCollectionSnapshot>(json, JsonOptions);
        }

        var collection = JsonSerializer.Deserialize<CollectionDefinition>(json, JsonOptions);
        if (collection is null)
        {
            return null;
        }

        return new DirtyCollectionSnapshot { Collection = collection };
    }

    private string GetFilePath(Guid collectionId) =>
        Path.Combine(DirtyDirectory, $"{collectionId:N}.json");

    private static void WriteTextAtomic(string path, string contents)
    {
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, contents);
            File.Copy(tempPath, path, overwrite: true);
            File.Delete(tempPath);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // ignore cleanup failures
            }

            throw;
        }
    }
}
