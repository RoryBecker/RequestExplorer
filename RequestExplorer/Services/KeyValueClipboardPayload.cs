using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using RequestExplorer.Models;

namespace RequestExplorer.Services;

public sealed class KeyValueClipboardRow
{
    public bool IsEnabled { get; set; } = true;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class KeyValueClipboardPayload
{
    private const string Marker = "RequestExplorer.KeyValueClipboard.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // In-process fallback — system clipboard can rewrite line endings / drop custom formats.
    private static KeyValueClipboardPayload? _appClipboard;

    public List<KeyValueClipboardRow> Rows { get; set; } = [];

    public static KeyValueClipboardPayload FromItems(IEnumerable<KeyValueItem> items) =>
        new()
        {
            Rows = items
                .Select(i => new KeyValueClipboardRow
                {
                    IsEnabled = i.IsEnabled,
                    Key = i.Key ?? string.Empty,
                    Value = i.Value ?? string.Empty
                })
                .ToList()
        };

    public void WriteToClipboard()
    {
        _appClipboard = this;

        var json = JsonSerializer.Serialize(this, JsonOptions);
        var text = Marker + "\n" + json + "\n" + BuildTsv();
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // App clipboard still holds the rows for in-app paste.
        }
    }

    public static bool TryReadFromClipboard(out KeyValueClipboardPayload? payload)
    {
        payload = null;

        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (TryParse(text, out payload))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Fall through to in-app clipboard.
        }

        if (_appClipboard?.Rows is { Count: > 0 })
        {
            payload = _appClipboard;
            return true;
        }

        return false;
    }

    private static bool TryParse(string? text, out KeyValueClipboardPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (normalized.StartsWith(Marker, StringComparison.Ordinal))
        {
            var rest = normalized[Marker.Length..].TrimStart('\n');
            var jsonEnd = rest.IndexOf('\n');
            var json = jsonEnd >= 0 ? rest[..jsonEnd] : rest;
            try
            {
                payload = JsonSerializer.Deserialize<KeyValueClipboardPayload>(json, JsonOptions);
                return payload?.Rows is { Count: > 0 };
            }
            catch
            {
                return false;
            }
        }

        // Fallback: tab/newline TSV (key\tvalue).
        var rows = new List<KeyValueClipboardRow>();
        foreach (var line in normalized.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('\t');
            var key = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            rows.Add(new KeyValueClipboardRow
            {
                IsEnabled = true,
                Key = key,
                Value = parts.Length > 1 ? parts[1] : string.Empty
            });
        }

        if (rows.Count == 0)
        {
            return false;
        }

        payload = new KeyValueClipboardPayload { Rows = rows };
        return true;
    }

    private string BuildTsv()
    {
        var sb = new StringBuilder();
        foreach (var row in Rows)
        {
            sb.Append(row.Key.Replace('\t', ' ').Replace('\n', ' '));
            sb.Append('\t');
            sb.Append(row.Value.Replace('\t', ' ').Replace('\n', ' '));
            sb.Append('\n');
        }

        return sb.ToString();
    }
}
