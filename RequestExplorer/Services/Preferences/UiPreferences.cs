namespace RequestExplorer.Services.Preferences;

public sealed class UiPreferences
{
    public bool IsQuickExportVisible { get; set; }

    public bool IsDarkMode { get; set; }

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

    /// <summary>
    /// Max nested <c>{{variable}}</c> lookups when resolving a value (clamped 1–50; default 10).
    /// </summary>
    public int VariableNestingMaxDepth { get; set; } = 10;

    /// <summary>When true, HTTP sends use an infinite timeout (cancel still works).</summary>
    public bool DevModeIgnoreTimeout { get; set; }

    /// <summary>When true, create a workspace zip backup after the main window is shown.</summary>
    public bool BackupOnStartup { get; set; } = true;

    /// <summary>How many backup zips to retain (clamped 1–100 when applied).</summary>
    public int BackupRetentionCount { get; set; } = 15;

    /// <summary>Tree node ids (collections/folders/requests) that were expanded when preferences were last saved.</summary>
    public List<Guid> ExpandedTreeNodeIds { get; set; } = [];

    /// <summary>Id of the selected tree node when preferences were last saved; null if none.</summary>
    public Guid? SelectedTreeNodeId { get; set; }

    /// <summary>Vertical scroll offset of the collection tree (NaN = unset).</summary>
    public double TreeScrollVerticalOffset { get; set; } = double.NaN;

    /// <summary>Max concurrent requests for Run All (clamped 1–10 when applied).</summary>
    public int RunAllMaxThreads { get; set; } = 1;

    /// <summary>When true, Run All includes timing expectations in Met.</summary>
    public bool RunAllCheckTiming { get; set; } = true;

    /// <summary>When true, Run All hides opted-out requests in the results grid.</summary>
    public bool RunAllHideOptedOut { get; set; }

    /// <summary>Run All dialog size/position (NaN left/top = center on owner).</summary>
    public double RunAllWindowWidth { get; set; } = 860;

    public double RunAllWindowHeight { get; set; } = 480;

    public double RunAllWindowLeft { get; set; } = double.NaN;

    public double RunAllWindowTop { get; set; } = double.NaN;

    /// <summary>Run All results grid column widths keyed by column header text.</summary>
    public Dictionary<string, double> RunAllColumnWidths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When true, mouse XButton1/XButton2 (4th/5th buttons) navigate tree selection history.
    /// Toolbar back/forward remain available either way.
    /// </summary>
    public bool EnableMouseTreeNavigation { get; set; } = true;

    /// <summary>
    /// Last kind chosen in the New dialog (Collection, Folder, or Request).
    /// </summary>
    public string LastNewItemKind { get; set; } = "Request";

    /// <summary>
    /// Experimental. When true, unsaved collections and undo history can be
    /// kept across launches (Dirty Save). Off until the user enables it.
    /// </summary>
    public bool EnableDirtySave { get; set; }

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
