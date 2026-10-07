using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RequestExplorer.Models;
using RequestExplorer.Services;
using RequestExplorer.Services.Undo;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Views;

/// <summary>
/// Ctrl+C / Ctrl+V and context-menu copy/paste of key/value rows across Params, Headers, and Variables grids.
/// </summary>
public static class KeyValueGridClipboard
{
    public static readonly DependencyProperty EnableRowClipboardProperty =
        DependencyProperty.RegisterAttached(
            "EnableRowClipboard",
            typeof(bool),
            typeof(KeyValueGridClipboard),
            new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnableRowClipboard(DependencyObject element, bool value) =>
        element.SetValue(EnableRowClipboardProperty, value);

    public static bool GetEnableRowClipboard(DependencyObject element) =>
        (bool)element.GetValue(EnableRowClipboardProperty);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        grid.PreviewKeyDown -= OnPreviewKeyDown;
        grid.PreviewMouseRightButtonDown -= OnPreviewMouseRightButtonDown;
        grid.ContextMenuOpening -= OnContextMenuOpening;
        grid.Loaded -= OnGridLoaded;

        if (e.NewValue is true)
        {
            grid.PreviewKeyDown += OnPreviewKeyDown;
            grid.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
            grid.ContextMenuOpening += OnContextMenuOpening;
            grid.Loaded += OnGridLoaded;
            EnsureContextMenu(grid);
        }
    }

    private static void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            EnsureContextMenu(grid);
        }
    }

    private static void EnsureContextMenu(DataGrid grid)
    {
        grid.ContextMenu ??= new ContextMenu();
        if (grid.ContextMenu.Items.OfType<MenuItem>().Any(m => Equals(m.Tag, "kv-copy")))
        {
            return;
        }

        if (grid.ContextMenu.Items.Count > 0)
        {
            grid.ContextMenu.Items.Add(new Separator());
        }

        var copy = new MenuItem { Header = "Copy rows", Tag = "kv-copy", InputGestureText = "Ctrl+C" };
        copy.Click += (_, _) => CopyRows(grid);
        var paste = new MenuItem { Header = "Paste rows", Tag = "kv-paste", InputGestureText = "Ctrl+V" };
        paste.Click += (_, _) => PasteRows(grid);
        grid.ContextMenu.Items.Add(copy);
        grid.ContextMenu.Items.Add(paste);
    }

    private static void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        // Right-click does not select by default; select the row under the cursor first.
        // Keep an existing multi-selection when the clicked row is already selected (copy/move).
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: KeyValueItemViewModel item })
        {
            KeyValueGridSelection.SelectForContextMenu(
                grid,
                item,
                addWithControl: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        }
    }

    private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid grid || grid.ContextMenu is null)
        {
            return;
        }

        try
        {
            EnsureContextMenu(grid);

            var canCopy = GetCopyableRows(grid).Count > 0;
            var canPaste = CanPasteInto(grid);

            foreach (var item in grid.ContextMenu.Items.OfType<MenuItem>().ToList())
            {
                if (Equals(item.Tag, "kv-copy"))
                {
                    item.IsEnabled = canCopy;
                }
                else if (Equals(item.Tag, "kv-paste"))
                {
                    item.IsEnabled = canPaste;
                }
            }
        }
        catch
        {
            // Never let clipboard menu wiring crash the grid right-click.
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        // Only leave Ctrl+C/V to the cell editor while a cell is actively being edited.
        if (IsEditingTextCell(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (CopyRows(grid))
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (PasteRows(grid))
            {
                e.Handled = true;
            }
        }
    }

    private static bool IsEditingTextCell(DependencyObject? source)
    {
        var cell = FindAncestor<DataGridCell>(source);
        return cell is { IsEditing: true } && Keyboard.FocusedElement is TextBox;
    }

    private static bool CopyRows(DataGrid grid)
    {
        var rows = GetCopyableRows(grid);
        if (rows.Count == 0)
        {
            return false;
        }

        KeyValueClipboardPayload.FromItems(rows.Select(r => new KeyValueItem
        {
            IsEnabled = r.IsEnabled,
            Key = r.Key,
            Value = r.Value
        })).WriteToClipboard();
        return true;
    }

    private static bool PasteRows(DataGrid grid)
    {
        if (!KeyValueClipboardPayload.TryReadFromClipboard(out var payload) || payload is null)
        {
            return false;
        }

        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);

        if (!TryGetPasteTarget(grid, out var request, out var listKind, out var container))
        {
            return false;
        }

        if (request is not null)
        {
            return request.PasteKeyValueRows(listKind, payload.Rows) > 0;
        }

        if (container is not null)
        {
            return container.PasteVariableRows(payload.Rows) > 0;
        }

        return false;
    }

    private static bool CanPasteInto(DataGrid grid) =>
        KeyValueClipboardPayload.TryReadFromClipboard(out _)
        && TryGetPasteTarget(grid, out _, out _, out _);

    private static bool TryGetPasteTarget(
        DataGrid grid,
        out RequestNodeViewModel? request,
        out KeyValueListKind listKind,
        out ContainerNodeViewModel? container)
    {
        request = null;
        container = null;
        listKind = default;

        var source = UnwrapItemsSource(grid.ItemsSource);

        if (grid.DataContext is RequestNodeViewModel req)
        {
            if (ReferenceEquals(source, req.Params))
            {
                request = req;
                listKind = KeyValueListKind.Params;
                return true;
            }

            if (ReferenceEquals(source, req.Headers))
            {
                request = req;
                listKind = KeyValueListKind.Headers;
                return true;
            }
        }

        if (grid.DataContext is ContainerNodeViewModel cont
            && ReferenceEquals(source, cont.Variables))
        {
            container = cont;
            return true;
        }

        return false;
    }

    private static object? UnwrapItemsSource(object? itemsSource) =>
        itemsSource is ICollectionView view ? view.SourceCollection : itemsSource;

    private static List<KeyValueItemViewModel> GetCopyableRows(DataGrid grid)
    {
        var selected = KeyValueGridSelection.SnapshotSelectedRows(grid)
            .Where(i => i.HasContent)
            .ToList();
        if (selected.Count > 0)
        {
            return selected;
        }

        if (grid.CurrentItem is KeyValueItemViewModel current && current.HasContent)
        {
            return [current];
        }

        return [];
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
