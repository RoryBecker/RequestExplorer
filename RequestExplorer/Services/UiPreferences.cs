namespace RequestExplorer.Services;

public sealed class UiPreferences
{
    public bool IsQuickExportVisible { get; set; }

    public double QuickExportWidth { get; set; } = 320;

    public double CollectionsWidth { get; set; } = 360;

    public double RequestPaneHeight { get; set; }

    public double ResponsePaneHeight { get; set; }

    public string? SelectedExportFormat { get; set; }

    /// <summary>Per-format wrap preference keyed by export format name.</summary>
    public Dictionary<string, bool> QuickExportWrapByFormat { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public double WindowWidth { get; set; } = 1200;

    public double WindowHeight { get; set; } = 800;

    public double WindowLeft { get; set; } = double.NaN;

    public double WindowTop { get; set; } = double.NaN;

    public string WindowState { get; set; } = "Normal";

    public bool TreeFilterIncludeAncestors { get; set; } = true;

    public bool TreeFilterIncludeName { get; set; } = true;

    public bool TreeFilterIncludeUrl { get; set; } = true;

    /// <summary>Global HTTP send timeout in seconds when <see cref="DevModeIgnoreTimeout"/> is false.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>When true, HTTP sends use an infinite timeout (cancel still works).</summary>
    public bool DevModeIgnoreTimeout { get; set; }

    /// <summary>When true, create a workspace zip backup after the main window is shown.</summary>
    public bool BackupOnStartup { get; set; } = true;

    /// <summary>How many backup zips to retain (clamped 1–100 when applied).</summary>
    public int BackupRetentionCount { get; set; } = 15;

    /// <summary>Tree node ids (collections/folders/requests) that were expanded when preferences were last saved.</summary>
    public List<Guid> ExpandedTreeNodeIds { get; set; } = [];

    public static bool DefaultQuickExportWrap(string? formatName) =>
        string.Equals(formatName, "CurlWindows", StringComparison.OrdinalIgnoreCase);

    public bool GetQuickExportWrap(string? formatName)
    {
        if (string.IsNullOrWhiteSpace(formatName))
        {
            return false;
        }

        if (QuickExportWrapByFormat.TryGetValue(formatName, out var wrap))
        {
            return wrap;
        }

        return DefaultQuickExportWrap(formatName);
    }

    public void SetQuickExportWrap(string formatName, bool wrap) =>
        QuickExportWrapByFormat[formatName] = wrap;
}
