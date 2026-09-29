using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RequestExplorer.Models;

namespace RequestExplorer.Services;

public sealed class CollectionPersistenceService
{
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

    public void EnsureStorageDirectory()
    {
        Directory.CreateDirectory(StorageDirectory);
    }

    public IReadOnlyList<CollectionDefinition> LoadAll()
    {
        EnsureStorageDirectory();

        var collections = new List<CollectionDefinition>();
        foreach (var path in Directory.EnumerateFiles(StorageDirectory, "*.json")
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
