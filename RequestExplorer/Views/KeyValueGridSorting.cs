using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Views;

/// <summary>
/// DataGrid sorting that keeps blank-key (spare) rows last, and parks disabled
/// keyed rows after enabled ones, so sort/save cannot fight the trailing spare row.
/// </summary>
public static class KeyValueGridSorting
{
    public static readonly DependencyProperty SpareAwareSortProperty =
        DependencyProperty.RegisterAttached(
            "SpareAwareSort",
            typeof(bool),
            typeof(KeyValueGridSorting),
            new PropertyMetadata(false, OnSpareAwareSortChanged));

    public static void SetSpareAwareSort(DependencyObject element, bool value) =>
        element.SetValue(SpareAwareSortProperty, value);

    public static bool GetSpareAwareSort(DependencyObject element) =>
        (bool)element.GetValue(SpareAwareSortProperty);

    private static void OnSpareAwareSortChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            grid.CanUserSortColumns = true;
            grid.Sorting += OnSorting;
        }
        else
        {
            grid.Sorting -= OnSorting;
        }
    }

    private static void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;

        if (sender is not DataGrid grid
            || grid.ItemsSource is not ObservableCollection<KeyValueItemViewModel> items
            || items.Count == 0)
        {
            return;
        }

        // Finish any in-cell edit before reordering so bindings/spare logic stay consistent.
        grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var column = e.Column;
        var direction = column.SortDirection != ListSortDirection.Ascending
            ? ListSortDirection.Ascending
            : ListSortDirection.Descending;

        foreach (var other in grid.Columns)
        {
            if (!ReferenceEquals(other, column))
            {
                other.SortDirection = null;
            }
        }

        column.SortDirection = direction;

        var property = ResolveSortProperty(column);
        if (property is null)
        {
            return;
        }

        ApplySort(items, property, direction);

        if (grid.DataContext is IKeyValueListSortHost host)
        {
            host.OnKeyValueListReordered(items);
        }
    }

    private static string? ResolveSortProperty(DataGridColumn column)
    {
        if (!string.IsNullOrWhiteSpace(column.SortMemberPath))
        {
            return column.SortMemberPath;
        }

        if (column is DataGridBoundColumn { Binding: Binding binding }
            && binding.Path?.Path is { Length: > 0 } path)
        {
            return path;
        }

        return null;
    }

    private static void ApplySort(
        ObservableCollection<KeyValueItemViewModel> items,
        string property,
        ListSortDirection direction)
    {
        var ordered = items
            .Select((item, index) => (item, index))
            .OrderBy(t => SortGroup(t.item))
            .ThenBy(t => SortKey(t.item, property), Comparer<object?>.Create((a, b) => CompareKeys(a, b, direction)))
            .ThenBy(t => t.index)
            .Select(t => t.item)
            .ToList();

        for (var target = 0; target < ordered.Count; target++)
        {
            var current = items.IndexOf(ordered[target]);
            if (current != target)
            {
                items.Move(current, target);
            }
        }
    }

    /// <summary>0 = enabled keyed, 1 = disabled keyed, 2 = blank key (spare).</summary>
    private static int SortGroup(KeyValueItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.Key))
        {
            return 2;
        }

        return item.IsEnabled ? 0 : 1;
    }

    private static object? SortKey(KeyValueItemViewModel item, string property) =>
        property switch
        {
            nameof(KeyValueItemViewModel.Key) => item.Key,
            nameof(KeyValueItemViewModel.Value) => item.Value,
            nameof(KeyValueItemViewModel.IsEnabled) => item.IsEnabled,
            nameof(KeyValueItemViewModel.HasKey) => item.HasKey,
            nameof(KeyValueItemViewModel.HasContent) => item.HasContent,
            _ => null
        };

    private static int CompareKeys(object? left, object? right, ListSortDirection direction)
    {
        int result;
        if (left is null && right is null)
        {
            result = 0;
        }
        else if (left is null)
        {
            result = -1;
        }
        else if (right is null)
        {
            result = 1;
        }
        else if (left is bool lb && right is bool rb)
        {
            result = lb.CompareTo(rb);
        }
        else
        {
            result = string.Compare(
                Convert.ToString(left),
                Convert.ToString(right),
                StringComparison.OrdinalIgnoreCase);
        }

        return direction == ListSortDirection.Ascending ? result : -result;
    }
}
