using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using RequestExplorer.Models;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Tree;

public sealed class TreeClipboardPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public TreeNodeKind Kind { get; set; }

    public CollectionDefinition? Collection { get; set; }

    public FolderDefinition? Folder { get; set; }

    public RequestDefinition? Request { get; set; }

    public string SourceName { get; set; } = string.Empty;

    public static TreeClipboardPayload FromCollection(CollectionDefinition model, string name) =>
        new()
        {
            Kind = TreeNodeKind.Collection,
            Collection = TreeNodeCloneService.CloneCollection(model),
            SourceName = name
        };

    public static TreeClipboardPayload FromFolder(FolderDefinition model, string name) =>
        new()
        {
            Kind = TreeNodeKind.Folder,
            Folder = TreeNodeCloneService.CloneFolder(model),
            SourceName = name
        };

    public static TreeClipboardPayload FromRequest(RequestDefinition model, string name) =>
        new()
        {
            Kind = TreeNodeKind.Request,
            Request = TreeNodeCloneService.CloneRequest(model),
            SourceName = name
        };

    public void WriteToClipboard()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        Clipboard.SetText($"RequestExplorer.TreeClipboard.v1\n{json}");
    }

    public static bool TryReadFromClipboard(out TreeClipboardPayload? payload)
    {
        payload = null;
        if (!Clipboard.ContainsText())
        {
            return false;
        }

        var text = Clipboard.GetText();
        const string prefix = "RequestExplorer.TreeClipboard.v1\n";
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<TreeClipboardPayload>(text[prefix.Length..], JsonOptions);
            return payload is not null;
        }
        catch
        {
            return false;
        }
    }
}
