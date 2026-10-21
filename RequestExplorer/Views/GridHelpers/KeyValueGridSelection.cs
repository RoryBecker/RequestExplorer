using System.Collections;
using System.Windows.Controls;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Views.GridHelpers;

/// <summary>Safe multi-select helpers for key/value DataGrids.</summary>
internal static class KeyValueGridSelection
{
    public static bool IsInSelection(DataGrid grid, object item)
    {
        try
        {
            return grid.SelectedItems.Contains(item);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Live selected key/value rows still present in the items source.</summary>
    public static List<KeyValueItemViewModel> SnapshotSelectedRows(DataGrid grid)
    {
        try
        {
            var live = GetLiveItemSet(grid);
            var result = new List<KeyValueItemViewModel>();
            foreach (var entry in grid.SelectedItems)
            {
                if (entry is KeyValueItemViewModel row && live.Contains(row))
                {
                    result.Add(row);
                }
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    public static void SelectForContextMenu(DataGrid grid, KeyValueItemViewModel item, bool addWithControl)
    {
        try
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        }
        catch
        {
            // Editing may already be closed or the row may be gone.
        }

        try
        {
            if (IsInSelection(grid, item))
            {
                grid.CurrentItem = item;
                return;
            }

            if (addWithControl)
            {
                if (!IsInSelection(grid, item))
                {
                    grid.SelectedItems.Add(item);
                }
            }
            else
            {
                // Prefer SelectedItem assignment over Clear+Add — safer with Extended selection.
                grid.SelectedItem = item;
            }

            grid.CurrentItem = item;
        }
        catch
        {
            try
            {
                grid.UnselectAll();
                grid.SelectedItem = item;
                grid.CurrentItem = item;
            }
            catch
            {
                // Leave selection alone rather than crash the context menu.
            }
        }
    }

    private static HashSet<object> GetLiveItemSet(DataGrid grid)
    {
        var live = new HashSet<object>();
        if (UnwrapItemsSource(grid.ItemsSource) is not IEnumerable source)
        {
            return live;
        }

        try
        {
            foreach (var entry in source)
            {
                if (entry is not null)
                {
                    live.Add(entry);
                }
            }
        }
        catch
        {
            // Ignore enumeration failures on a mutating source.
        }

        return live;
    }

    private static object? UnwrapItemsSource(object? itemsSource) =>
        itemsSource is System.ComponentModel.ICollectionView view
            ? view.SourceCollection
            : itemsSource;
}
