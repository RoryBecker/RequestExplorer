using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RequestExplorer.Models;

namespace RequestExplorer.Services;

public sealed class CollectionPersistenceService
{
    private const string OrderFileName = "collection-order.json";
    private const int MaxFileNameLength = 120;

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public CollectionPersistenceService(string? storageDirectory = null)
    {
        StorageDirectory = storageDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RequestExplorer",
                "collections");
    }

    public string StorageDirectory { get; }

    private string OrderFilePath => Path.Combine(StorageDirectory, OrderFileName);

    public void EnsureStorageDirectory()
    {
        Directory.CreateDirectory(StorageDirectory);
    }

    public IReadOnlyList<CollectionDefinition> LoadAll()
    {
        EnsureStorageDirectory();

        var collections = new List<CollectionDefinition>();
        foreach (var path in EnumerateCollectionFiles())
        {
            try
            {
                var json = File.ReadAllText(path);
                var collection = JsonSerializer.Deserialize<CollectionDefinition>(json, JsonOptions);
                if (collection is not null)
                {
                    collections.Add(collection);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load collection file '{path}'.", ex);
            }
        }

        return collections;
    }

    public IReadOnlyList<Guid> LoadCollectionOrder()
    {
        EnsureStorageDirectory();
        if (!File.Exists(OrderFilePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(OrderFilePath);
            var document = JsonSerializer.Deserialize<CollectionOrderDocument>(json, JsonOptions);
            return document?.CollectionIds ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveCollectionOrder(IEnumerable<Guid> collectionIds)
    {
        EnsureStorageDirectory();
        var document = new CollectionOrderDocument
        {
            CollectionIds = collectionIds.ToList()
        };
        var json = JsonSerializer.Serialize(document, JsonOptions);
        WriteTextAtomic(OrderFilePath, json);
    }

    /// <summary>
    /// Applies a preferred order. IDs not present in <paramref name="preferredOrder"/>
    /// are placed at the start (sorted by name); remaining known IDs keep preferred order.
    /// </summary>
    public static IReadOnlyList<CollectionDefinition> ApplyCollectionOrder(
        IReadOnlyList<CollectionDefinition> loaded,
        IReadOnlyList<Guid> preferredOrder)
    {
        if (loaded.Count == 0)
        {
            return loaded;
        }

        var byId = new Dictionary<Guid, CollectionDefinition>();
        foreach (var collection in loaded)
        {
            byId[collection.Id] = collection;
        }

        var remaining = new HashSet<Guid>(byId.Keys);
        var known = new List<CollectionDefinition>();
        foreach (var id in preferredOrder)
        {
            if (byId.TryGetValue(id, out var collection))
            {
                known.Add(collection);
                remaining.Remove(id);
            }
        }

        var newcomers = remaining
            .Select(id => byId[id])
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id)
            .ToList();

        newcomers.AddRange(known);
        return newcomers;
    }

    private sealed class CollectionOrderDocument
    {
        public List<Guid> CollectionIds { get; set; } = [];
    }

    public void Save(CollectionDefinition collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        EnsureStorageDirectory();

        var path = AllocateFilePath(collection.Name, collection.Id);
        var json = JsonSerializer.Serialize(collection, JsonOptions);
        try
        {
            WriteTextAtomic(path, json);
            DeleteStaleFilesForId(collection.Id, keepPath: path);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save collection '{collection.Name}'.", ex);
        }
    }

    /// <summary>
    /// Renames/moves the on-disk file for a collection to match <paramref name="newName"/>,
    /// updating only the Name field in the existing JSON (preserves other on-disk content).
    /// Returns false if no file exists yet for this id.
    /// </summary>
    public bool SyncFileNameToCollectionName(Guid collectionId, string newName)
    {
        EnsureStorageDirectory();
        var existing = FindPathById(collectionId);
        if (existing is null)
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(existing);
            var collection = JsonSerializer.Deserialize<CollectionDefinition>(json, JsonOptions);
            if (collection is null)
            {
                return false;
            }

            collection.Name = newName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(collection.Name))
            {
                collection.Name = "Collection";
            }

            var path = AllocateFilePath(collection.Name, collection.Id);
            WriteTextAtomic(path, JsonSerializer.Serialize(collection, JsonOptions));
            DeleteStaleFilesForId(collection.Id, keepPath: path);
            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to rename collection file to '{newName}'.",
                ex);
        }
    }

    public void Delete(Guid collectionId)
    {
        EnsureStorageDirectory();
        foreach (var path in FindAllPathsById(collectionId))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to delete collection file '{path}'.",
                    ex);
            }
        }
    }

    /// <summary>Preferred on-disk path for a collection name (may include id suffix on collision).</summary>
    public string GetFilePath(string collectionName, Guid collectionId) =>
        AllocateFilePath(collectionName, collectionId);

    /// <summary>Legacy helper — prefers an existing file for the id, otherwise a name-less id path.</summary>
    public string GetFilePath(Guid collectionId) =>
        FindPathById(collectionId) ?? Path.Combine(StorageDirectory, $"{collectionId:N}.json");

    private IEnumerable<string> EnumerateCollectionFiles() =>
        Directory.EnumerateFiles(StorageDirectory, "*.json")
            .Where(p => !string.Equals(Path.GetFileName(p), OrderFileName, StringComparison.OrdinalIgnoreCase));

    private string? FindPathById(Guid collectionId) =>
        FindAllPathsById(collectionId).FirstOrDefault();

    private List<string> FindAllPathsById(Guid collectionId)
    {
        var matches = new List<string>();
        foreach (var path in EnumerateCollectionFiles())
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("id", out var idProp)
                    && Guid.TryParse(idProp.GetString(), out var id)
                    && id == collectionId)
                {
                    matches.Add(path);
                }
            }
            catch
            {
                // skip unreadable
            }
        }

        return matches;
    }

    private void DeleteStaleFilesForId(Guid collectionId, string keepPath)
    {
        foreach (var path in FindAllPathsById(collectionId))
        {
            if (string.Equals(path, keepPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // leave stale file; next save may retry
            }
        }
    }

    private string AllocateFilePath(string? collectionName, Guid collectionId)
    {
        var safe = SanitizeFileName(collectionName);
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = collectionId.ToString("N");
        }

        var candidate = Path.Combine(StorageDirectory, safe + ".json");
        if (CanUsePath(candidate, collectionId))
        {
            return candidate;
        }

        var withId = Path.Combine(StorageDirectory, $"{safe}-{collectionId.ToString("N")[..8]}.json");
        return withId;
    }

    private bool CanUsePath(string path, Guid collectionId)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("id", out var idProp)
                && Guid.TryParse(idProp.GetString(), out var id))
            {
                return id == collectionId;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    internal static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Trim().Length);
        foreach (var ch in name.Trim())
        {
            if (ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'
                || char.IsControl(ch))
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(ch);
            }
        }

        var safe = Regex.Replace(sb.ToString(), @"\s+", " ").Trim().TrimEnd('.');
        if (safe.Length > MaxFileNameLength)
        {
            safe = safe[..MaxFileNameLength].TrimEnd().TrimEnd('.');
        }

        if (WindowsReservedNames.Contains(safe))
        {
            safe = "_" + safe;
        }

        return safe;
    }

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
