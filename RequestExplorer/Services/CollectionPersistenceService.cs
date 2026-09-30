using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RequestExplorer.Models;

namespace RequestExplorer.Services;

public sealed class CollectionPersistenceService
{
    private const string OrderFileName = "collection-order.json";

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
        foreach (var path in Directory.EnumerateFiles(StorageDirectory, "*.json")
                     .Where(p => !string.Equals(Path.GetFileName(p), OrderFileName, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
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
        var tempPath = OrderFilePath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);
            File.Copy(tempPath, OrderFilePath, overwrite: true);
            File.Delete(tempPath);
        }
        catch (Exception ex)
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

            throw new InvalidOperationException("Failed to save collection order.", ex);
        }
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

        var path = GetFilePath(collection.Id);
        var json = JsonSerializer.Serialize(collection, JsonOptions);
        var tempPath = path + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);
            File.Copy(tempPath, path, overwrite: true);
            File.Delete(tempPath);
        }
        catch (Exception ex)
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

            throw new InvalidOperationException($"Failed to save collection '{collection.Name}'.", ex);
        }
    }

    public void Delete(Guid collectionId)
    {
        var path = GetFilePath(collectionId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public string GetFilePath(Guid collectionId) =>
        Path.Combine(StorageDirectory, $"{collectionId:N}.json");
}
