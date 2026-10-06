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
using RequestExplorer.ViewModels;

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

    /// <summary>✓ when the response status matches the request's expected status code.</summary>
    [ObservableProperty]
    private string _metDisplay = string.Empty;

    public void SetExpectationMet(bool? met) =>
        MetDisplay = met == true ? "✓" : string.Empty;

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
            nameof(RunAllRequestResultRow.MetDisplay) => row.MetDisplay,
            _ => string.Empty
        };

    /// <summary>0 = unmet / blank, 1 = met (✓).</summary>
    private static int MetRank(string? metDisplay) =>
        string.IsNullOrEmpty(metDisplay) ? 0 : 1;

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
    private readonly ListCollectionView _view;
    private readonly RunAllRowComparer _comparer = new();
    private bool _finished;
    private bool _stopRequested;
    private string? _sortColumn;

    public RunAllResultsDialog(
        string title,
        ObservableCollection<RunAllRequestResultRow> rows,
        Action? cancelRequested = null)
    {
        InitializeComponent();
        Title = title;
        SummaryText.Text = "Starting…";
        _cancelRequested = cancelRequested;
        _view = (ListCollectionView)CollectionViewSource.GetDefaultView(rows);
        _view.CustomSort = _comparer;
        ResultsGrid.ItemsSource = _view;
        ResultsGrid.Sorting += ResultsGrid_OnSorting;
        Closing += OnClosing;
        ApplySortIndicators();
    }

    /// <summary>Request chosen via double-click; set before the dialog closes.</summary>
    public RequestNodeViewModel? NavigatedNode { get; private set; }

    public void SetSummary(string text) => SummaryText.Text = text;

    public void MarkFinished()
    {
        _finished = true;
        CancelButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.IsEnabled = true;
        CloseButton.IsDefault = true;
        CloseButton.IsCancel = true;
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

        _stopRequested = true;
        CancelButton.IsEnabled = false;
        SetSummary("Stopping remaining requests…");
        _cancelRequested?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_finished)
        {
            _cancelRequested?.Invoke();
        }
    }
}
