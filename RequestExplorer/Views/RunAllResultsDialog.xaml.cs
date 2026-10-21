using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.Services;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Views;

public enum RunAllSortPhase
{
    /// <summary>Original enumeration order under the Run All root.</summary>
    TreeOrder,

    /// <summary>Text: A–Z. Numeric: largest first.</summary>
    Primary,

    /// <summary>Text: Z–A. Numeric: smallest first.</summary>
    Secondary
}

public partial class RunAllRequestResultRow : ObservableObject
{
    public required RequestNodeViewModel Request { get; init; }

    /// <summary>0-based order when the run started (tree walk).</summary>
    public int TreeOrder { get; init; }

    /// <summary>Ancestor path above this request (excludes the request name).</summary>
    public string HierarchyDisplay { get; init; } = string.Empty;

    /// <summary>True when this request (or an ancestor folder) opted out of auto run.</summary>
    public bool IsOptedOut { get; init; }

    public string HierarchyPath => Request.HierarchyPath;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _statusDisplay = "Pending";

    [ObservableProperty]
    private string _elapsedDisplay = "—";

    /// <summary>Actual duration in ms for sorting; null when unknown / not run.</summary>
    [ObservableProperty]
    private double? _elapsedMilliseconds;

    [ObservableProperty]
    private bool _succeeded;

    /// <summary>✓ when all enabled expectations are met; ✗ when any failed.</summary>
    [ObservableProperty]
    private string _metDisplay = string.Empty;

    /// <summary>✓/✗ when a body expectation was checked; empty otherwise.</summary>
    [ObservableProperty]
    private string _bodyDisplay = string.Empty;

    [ObservableProperty]
    private bool _isStatusExpectationFailed;

    [ObservableProperty]
    private bool _isStatusExpectationMet;

    [ObservableProperty]
    private bool _isBodyExpectationFailed;

    [ObservableProperty]
    private bool _isBodyExpectationMet;

    [ObservableProperty]
    private bool _isTimingExpectationFailed;

    [ObservableProperty]
    private bool _isTimingExpectationMet;

    public void ApplyExpectationResult(ExpectationCheckResult? result)
    {
        if (result is null)
        {
            MetDisplay = string.Empty;
            BodyDisplay = string.Empty;
            IsStatusExpectationFailed = false;
            IsStatusExpectationMet = false;
            IsBodyExpectationFailed = false;
            IsBodyExpectationMet = false;
            IsTimingExpectationFailed = false;
            IsTimingExpectationMet = false;
            return;
        }

        MetDisplay = result.IsMet ? "✓" : "✗";
        IsStatusExpectationFailed = result.StatusFailed;
        IsStatusExpectationMet = !result.StatusFailed;

        if (result.BodyChecked)
        {
            BodyDisplay = result.BodyFailed ? "✗" : "✓";
            IsBodyExpectationFailed = result.BodyFailed;
            IsBodyExpectationMet = !result.BodyFailed;
        }
        else
        {
            BodyDisplay = string.Empty;
            IsBodyExpectationFailed = false;
            IsBodyExpectationMet = false;
        }

        IsTimingExpectationFailed = result.TimingFailed;
        IsTimingExpectationMet = result.TimingChecked && !result.TimingFailed;
    }

    public void ClearTiming()
    {
        ElapsedDisplay = "—";
        ElapsedMilliseconds = null;
    }

    public void SetTiming(double totalMilliseconds, string elapsedDisplay)
    {
        ElapsedMilliseconds = totalMilliseconds;
        ElapsedDisplay = elapsedDisplay;
    }

    public static string BuildHierarchyDisplay(RequestNodeViewModel request)
    {
        var names = new List<string>();
        for (var node = request.Parent; node is not null; node = node.Parent)
        {
            names.Add(node.Name);
        }

        names.Reverse();
        return string.Join(" / ", names);
    }
}

public sealed class RunAllRowComparer : IComparer
{
    public string? Column { get; set; }

    public RunAllSortPhase Phase { get; set; } = RunAllSortPhase.TreeOrder;

    public bool IsNumericColumn { get; set; }

    public bool IsMetColumn { get; set; }

    public int Compare(object? x, object? y)
    {
        var a = x as RunAllRequestResultRow;
        var b = y as RunAllRequestResultRow;
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a is null)
        {
            return 1;
        }

        if (b is null)
        {
            return -1;
        }

        if (Phase == RunAllSortPhase.TreeOrder || string.IsNullOrEmpty(Column))
        {
            return a.TreeOrder.CompareTo(b.TreeOrder);
        }

        int cmp;
        if (IsMetColumn)
        {
            // Primary: unmet first. Secondary: met first.
            cmp = MetRank(a.MetDisplay).CompareTo(MetRank(b.MetDisplay));
            if (Phase == RunAllSortPhase.Secondary)
            {
                cmp = -cmp;
            }
        }
        else if (IsNumericColumn)
        {
            // Primary: largest first. Secondary: smallest first.
            cmp = CompareMilliseconds(a.ElapsedMilliseconds, b.ElapsedMilliseconds);
            if (Phase == RunAllSortPhase.Primary)
            {
                cmp = -cmp;
            }
        }
        else
        {
            // Primary: A–Z. Secondary: Z–A.
            cmp = CompareText(GetText(a), GetText(b));
            if (Phase == RunAllSortPhase.Secondary)
            {
                cmp = -cmp;
            }
        }

        if (cmp != 0)
        {
            return cmp;
        }

        return a.TreeOrder.CompareTo(b.TreeOrder);
    }

    private string GetText(RunAllRequestResultRow row) =>
        Column switch
        {
            nameof(RunAllRequestResultRow.HierarchyDisplay) => row.HierarchyDisplay,
            nameof(RunAllRequestResultRow.Name) => row.Name,
            nameof(RunAllRequestResultRow.StatusDisplay) => row.StatusDisplay,
            nameof(RunAllRequestResultRow.BodyDisplay) => row.BodyDisplay,
            nameof(RunAllRequestResultRow.MetDisplay) => row.MetDisplay,
            _ => string.Empty
        };

    /// <summary>0 = unmet (✗ or blank), 1 = met (✓).</summary>
    private static int MetRank(string? metDisplay) =>
        string.Equals(metDisplay, "✓", StringComparison.Ordinal) ? 1 : 0;

    private static int CompareText(string? a, string? b) =>
        string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

    private static int CompareMilliseconds(double? a, double? b)
    {
        if (a is null && b is null)
        {
            return 0;
        }

        // Unknown durations sort after known values in both directions.
        if (a is null)
        {
            return 1;
        }

        if (b is null)
        {
            return -1;
        }

        return a.Value.CompareTo(b.Value);
    }
}

public partial class RunAllResultsDialog : Window
{
    private readonly Action? _cancelRequested;
    private readonly Func<Task>? _rerunRequested;
    private readonly Func<RunAllRequestResultRow, Task>? _rerunRowRequested;
    private readonly ListCollectionView _view;
    private readonly RunAllRowComparer _comparer = new();
    private bool _finished;
    private bool _stopRequested;
    private bool _rerunInFlight;
    private string? _sortColumn;

    public RunAllResultsDialog(
        string title,
        ObservableCollection<RunAllRequestResultRow> rows,
        Action? cancelRequested = null,
        Func<Task>? rerunRequested = null,
        Func<RunAllRequestResultRow, Task>? rerunRowRequested = null,
        int initialMaxThreads = 1,
        bool initialCheckTiming = true,
        bool initialHideOptedOut = false,
        double initialWidth = double.NaN,
        double initialHeight = double.NaN,
        double initialLeft = double.NaN,
        double initialTop = double.NaN,
        IReadOnlyDictionary<string, double>? initialColumnWidths = null)
    {
        InitializeComponent();
        Title = title;
        SummaryText.Text = "Starting…";
        _cancelRequested = cancelRequested;
        _rerunRequested = rerunRequested;
        _rerunRowRequested = rerunRowRequested;
        for (var i = 1; i <= 10; i++)
        {
            MaxThreadsBox.Items.Add(i);
        }

        MaxThreadsBox.SelectedItem = Math.Clamp(initialMaxThreads, 1, 10);
        CheckTimingBox.IsChecked = initialCheckTiming;
        HideOptedOutBox.IsChecked = initialHideOptedOut;
        ApplyWindowBounds(initialWidth, initialHeight, initialLeft, initialTop);
        ApplyColumnWidths(initialColumnWidths);
        _view = (ListCollectionView)CollectionViewSource.GetDefaultView(rows);
        _view.CustomSort = _comparer;
        _view.Filter = FilterRow;
        ResultsGrid.ItemsSource = _view;
        HideOptedOutBox.Checked += HideOptedOutBox_OnChanged;
        HideOptedOutBox.Unchecked += HideOptedOutBox_OnChanged;
        ResultsGrid.Sorting += ResultsGrid_OnSorting;
        Closing += OnClosing;
        ApplySortIndicators();
        MarkRunning();
    }

    /// <summary>Captures the current normal-state size and position for persistence.</summary>
    public void CaptureWindowBounds(out double width, out double height, out double left, out double top)
    {
        if (WindowState == WindowState.Normal)
        {
            width = Width;
            height = Height;
            left = Left;
            top = Top;
            return;
        }

        if (RestoreBounds is { Width: > 0, Height: > 0 } bounds)
        {
            width = bounds.Width;
            height = bounds.Height;
            left = bounds.Left;
            top = bounds.Top;
            return;
        }

        width = Width;
        height = Height;
        left = Left;
        top = Top;
    }

    /// <summary>Captures current column pixel widths keyed by header text.</summary>
    public Dictionary<string, double> CaptureColumnWidths()
    {
        var widths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in ResultsGrid.Columns)
        {
            var key = GetColumnKey(column);
            if (key is null)
            {
                continue;
            }

            var width = column.ActualWidth;
            if (width < 24)
            {
                width = column.Width.IsAbsolute ? column.Width.Value : 24;
            }

            widths[key] = width;
        }

        return widths;
    }

    private void ApplyColumnWidths(IReadOnlyDictionary<string, double>? widths)
    {
        if (widths is null || widths.Count == 0)
        {
            return;
        }

        foreach (var column in ResultsGrid.Columns)
        {
            var key = GetColumnKey(column);
            if (key is null || !widths.TryGetValue(key, out var width) || width < 24)
            {
                continue;
            }

            column.Width = new DataGridLength(width);
        }
    }

    private static string? GetColumnKey(DataGridColumn column) =>
        column.Header switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text.Trim(),
            _ => null
        };

    private void ApplyWindowBounds(double width, double height, double left, double top)
    {
        if (width >= MinWidth)
        {
            Width = width;
        }

        if (height >= MinHeight)
        {
            Height = height;
        }

        if (!double.IsNaN(left)
            && !double.IsNaN(top)
            && IsWindowOnScreen(left, top, Width, Height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
    }

    private static bool IsWindowOnScreen(double left, double top, double width, double height)
    {
        var work = SystemParameters.WorkArea;
        var right = left + Math.Max(width, 100);
        var bottom = top + Math.Max(height, 100);
        return right > work.Left + 40
               && left < work.Right - 40
               && bottom > work.Top + 40
               && top < work.Bottom - 40;
    }

    /// <summary>Request chosen via double-click; set before the dialog closes.</summary>
    public RequestNodeViewModel? NavigatedNode { get; private set; }

    /// <summary>When true, timing expectations count toward Met for this run.</summary>
    public bool CheckTiming => CheckTimingBox.IsChecked == true;

    /// <summary>When true, opted-out requests are hidden in the results grid.</summary>
    public bool HideOptedOut => HideOptedOutBox.IsChecked == true;

    private void HideOptedOutBox_OnChanged(object sender, RoutedEventArgs e) =>
        _view.Refresh();

    private bool FilterRow(object item) =>
        item is RunAllRequestResultRow row && (!HideOptedOut || !row.IsOptedOut);

    /// <summary>Max concurrent requests for a pass (1–10).</summary>
    public int MaxThreads
    {
        get
        {
            if (MaxThreadsBox.SelectedItem is int value)
            {
                return Math.Clamp(value, 1, 10);
            }

            return 1;
        }
    }

    public void SetSummary(string text) => SummaryText.Text = text;

    public void MarkRunning()
    {
        _finished = false;
        _stopRequested = false;
        CancelButton.IsEnabled = true;
        CancelButton.Visibility = Visibility.Visible;
        RerunButton.IsEnabled = false;
        RerunButton.Visibility = Visibility.Collapsed;
        // Keep Close available so dismissing the window can abandon the run.
        CloseButton.IsEnabled = true;
        CloseButton.IsDefault = false;
        CloseButton.IsCancel = false;
        MaxThreadsBox.IsEnabled = false;
        CheckTimingBox.IsEnabled = false;
    }

    public void MarkFinished()
    {
        _finished = true;
        _rerunInFlight = false;
        CancelButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Collapsed;
        RerunButton.Visibility = Visibility.Visible;
        RerunButton.IsEnabled = _rerunRequested is not null;
        CloseButton.IsEnabled = true;
        CloseButton.IsDefault = true;
        CloseButton.IsCancel = true;
        MaxThreadsBox.IsEnabled = true;
        CheckTimingBox.IsEnabled = true;
    }

    /// <summary>Cancels any in-flight Run All work (safe to call more than once).</summary>
    public void RequestCancel()
    {
        if (_stopRequested && _finished)
        {
            return;
        }

        if (!_finished)
        {
            _stopRequested = true;
            CancelButton.IsEnabled = false;
            SetSummary("Stopping remaining requests…");
        }

        _cancelRequested?.Invoke();
    }

    private void ResultsGrid_OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;

        var columnKey = e.Column.SortMemberPath;
        if (string.IsNullOrEmpty(columnKey))
        {
            return;
        }

        var isNumeric = string.Equals(
            columnKey,
            nameof(RunAllRequestResultRow.ElapsedMilliseconds),
            StringComparison.Ordinal);
        var isMet = string.Equals(
            columnKey,
            nameof(RunAllRequestResultRow.MetDisplay),
            StringComparison.Ordinal);

        if (!string.Equals(_sortColumn, columnKey, StringComparison.Ordinal))
        {
            _sortColumn = columnKey;
            _comparer.Phase = RunAllSortPhase.Primary;
        }
        else
        {
            _comparer.Phase = _comparer.Phase switch
            {
                RunAllSortPhase.TreeOrder => RunAllSortPhase.Primary,
                RunAllSortPhase.Primary => RunAllSortPhase.Secondary,
                _ => RunAllSortPhase.TreeOrder
            };

            if (_comparer.Phase == RunAllSortPhase.TreeOrder)
            {
                _sortColumn = null;
            }
        }

        _comparer.Column = _sortColumn;
        _comparer.IsNumericColumn = isNumeric && _sortColumn is not null;
        _comparer.IsMetColumn = isMet && _sortColumn is not null;
        _view.Refresh();
        ApplySortIndicators();
    }

    private void ApplySortIndicators()
    {
        foreach (var column in ResultsGrid.Columns)
        {
            if (string.IsNullOrEmpty(column.SortMemberPath))
            {
                continue;
            }

            if (_comparer.Phase == RunAllSortPhase.TreeOrder
                || !string.Equals(column.SortMemberPath, _comparer.Column, StringComparison.Ordinal))
            {
                column.SortDirection = null;
                continue;
            }

            var isNumeric = string.Equals(
                column.SortMemberPath,
                nameof(RunAllRequestResultRow.ElapsedMilliseconds),
                StringComparison.Ordinal);

            // Match WPF glyph to the active order:
            // text / Met Primary → Ascending; Secondary → Descending
            // numeric Primary largest → Descending; Secondary smallest → Ascending
            column.SortDirection = isNumeric
                ? _comparer.Phase == RunAllSortPhase.Primary
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending
                : _comparer.Phase == RunAllSortPhase.Primary
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;
        }
    }

    private void ResultsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindRowItem(e.OriginalSource as DependencyObject) is not RunAllRequestResultRow row)
        {
            return;
        }

        e.Handled = true;
        NavigatedNode = row.Request;
        DialogResult = true;
    }

    private static RunAllRequestResultRow? FindRowItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is DataGridRow { Item: RunAllRequestResultRow row })
            {
                return row;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_finished || _stopRequested)
        {
            return;
        }

        RequestCancel();
    }

    private async void Rerun_Click(object sender, RoutedEventArgs e)
    {
        if (!_finished || _rerunInFlight || _rerunRequested is null)
        {
            return;
        }

        _rerunInFlight = true;
        RerunButton.IsEnabled = false;
        try
        {
            await _rerunRequested();
        }
        finally
        {
            _rerunInFlight = false;
        }
    }

    private void ResultsGrid_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (!_finished || _rerunInFlight || _rerunRowRequested is null)
        {
            e.Handled = true;
            return;
        }

        if (FindRowItem(e.OriginalSource as DependencyObject) is RunAllRequestResultRow { IsOptedOut: true })
        {
            e.Handled = true;
        }
    }

    private async void RerunRowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!_finished || _rerunInFlight || _rerunRowRequested is null)
        {
            return;
        }

        var row = ResolveRowFromMenu(sender) ?? ResultsGrid.SelectedItem as RunAllRequestResultRow;
        if (row is null)
        {
            return;
        }

        _rerunInFlight = true;
        RerunButton.IsEnabled = false;
        try
        {
            await _rerunRowRequested(row);
        }
        finally
        {
            _rerunInFlight = false;
            if (_finished)
            {
                RerunButton.IsEnabled = _rerunRequested is not null;
            }
        }
    }

    private static RunAllRequestResultRow? ResolveRowFromMenu(object sender)
    {
        if (sender is not MenuItem menuItem)
        {
            return null;
        }

        if (menuItem.Parent is ContextMenu { PlacementTarget: DataGridRow row })
        {
            return row.Item as RunAllRequestResultRow;
        }

        return menuItem.DataContext as RunAllRequestResultRow;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!_finished)
        {
            RequestCancel();
        }

        DialogResult = true;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Title-bar / Alt+F4 / programmatic close — always abandon remaining work.
        if (!_finished)
        {
            RequestCancel();
        }
    }
}
