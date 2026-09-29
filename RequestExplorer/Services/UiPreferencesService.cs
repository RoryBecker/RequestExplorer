using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RequestExplorer.Services;

public sealed class UiPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public UiPreferencesService(string? filePath = null)
    {
        FilePath = filePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RequestExplorer",
                "preferences.json");
    }

    public string FilePath { get; }

    public UiPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new UiPreferences();
            }

            var json = File.ReadAllText(FilePath);
            var prefs = JsonSerializer.Deserialize<UiPreferences>(json, JsonOptions) ?? new UiPreferences();
            prefs.QuickExportWrapByFormat ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            return prefs;
        }
        catch
        {
            return new UiPreferences();
        }
    }

    public void Save(UiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(preferences, JsonOptions);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Copy(tempPath, FilePath, overwrite: true);
        File.Delete(tempPath);
    }
}
