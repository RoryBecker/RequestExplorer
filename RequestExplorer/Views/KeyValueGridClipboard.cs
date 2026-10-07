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
/// Also provides Copy Name / Copy Value for the focused row (including inherited-variable grids).
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
        var menu = grid.ContextMenu;
        var existing = menu.Items.OfType<MenuItem>().Select(m => m.Tag).ToHashSet();

        if (!existing.Contains("kv-copy-name"))
        {
            var insertAt = IndexOfFirstRowClipboardItem(menu);
            menu.Items.Insert(insertAt, CreateMenuItem("Copy _Name", "kv-copy-name", () => CopyField(grid, copyValue: false)));
            menu.Items.Insert(insertAt + 1, CreateMenuItem("Copy _Value", "kv-copy-value", () => CopyField(grid, copyValue: true)));
        }

        existing = menu.Items.OfType<MenuItem>().Select(m => m.Tag).ToHashSet();
        if (!existing.Contains("kv-goto-definition"))
        {
            var insertAt = IndexOfFirstRowClipboardItem(menu);
            // Place after Copy Name/Value when those were just inserted above.
            var nameIndex = IndexOfTaggedMenuItem(menu, "kv-copy-value");
            if (nameIndex >= 0)
            {
                insertAt = nameIndex + 1;
            }

            menu.Items.Insert(
                insertAt,
                CreateMenuItem("Go to _Definition", "kv-goto-definition", () => GoToDefinition(grid)));
        }

        existing = menu.Items.OfType<MenuItem>().Select(m => m.Tag).ToHashSet();
        if (existing.Contains("kv-copy"))
        {
            return;
        }

        if (menu.Items.Count > 0 && menu.Items[^1] is not Separator)
        {
            menu.Items.Add(new Separator { Tag = "kv-clipboard-sep" });
        }

        menu.Items.Add(CreateMenuItem("Copy rows", "kv-copy", () => CopyRows(grid), "Ctrl+C"));
        menu.Items.Add(CreateMenuItem("Paste rows", "kv-paste", () => PasteRows(grid), "Ctrl+V"));
    }

    private static MenuItem CreateMenuItem(string header, string tag, Action onClick, string? gesture = null)
    {
        var item = new MenuItem { Header = header, Tag = tag, InputGestureText = gesture };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static int IndexOfFirstRowClipboardItem(ContextMenu menu)
    {
        for (var i = 0; i < menu.Items.Count; i++)
        {
            if (menu.Items[i] is MenuItem { Tag: string tag } && tag is "kv-copy" or "kv-paste")
            {
                return i;
            }
        }

        return menu.Items.Count;
    }

    private static int IndexOfTaggedMenuItem(ContextMenu menu, string tag)
    {
        for (var i = 0; i < menu.Items.Count; i++)
        {
            if (menu.Items[i] is MenuItem item && Equals(item.Tag, tag))
            {
                return i;
            }
        }

        return -1;
    }

    private static void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        // Right-click does not select by default; select the row under the cursor first.
        // Keep an existing multi-selection when the clicked row is already selected (copy/move).
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not { } row)
        {
            return;
        }

        switch (row.Item)
        {
            case KeyValueItemViewModel item:
                KeyValueGridSelection.SelectForContextMenu(
                    grid,
                    item,
                    addWithControl: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                break;
            case InheritedVariableRow inherited:
                try
                {
                    grid.SelectedItem = inherited;
                    grid.CurrentItem = inherited;
                }
                catch
                {
                    // ignore selection failures
                }

                break;
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

            var canCopyRows = GetCopyableRows(grid).Count > 0;
            var canPaste = CanPasteInto(grid);
            var canCopyField = TryGetPrimaryNameValue(grid, out _, out _);
            var isInherited = IsInheritedVariablesGrid(grid);
            var canGoToDefinition = isInherited && TryGetInheritedSelection(grid, out _);

            // Inherited grids only need Name/Value + Go to Definition; hide row copy/paste there.
            var showRowClipboard = !isInherited;

            foreach (var item in grid.ContextMenu.Items.OfType<MenuItem>().ToList())
            {
                if (Equals(item.Tag, "kv-copy"))
                {
                    item.Visibility = showRowClipboard ? Visibility.Visible : Visibility.Collapsed;
                    item.IsEnabled = canCopyRows;
                }
                else if (Equals(item.Tag, "kv-paste"))
                {
                    item.Visibility = showRowClipboard ? Visibility.Visible : Visibility.Collapsed;
                    item.IsEnabled = canPaste;
                }
                else if (Equals(item.Tag, "kv-copy-name") || Equals(item.Tag, "kv-copy-value"))
                {
                    item.IsEnabled = canCopyField;
                }
                else if (Equals(item.Tag, "kv-goto-definition"))
                {
                    item.Visibility = isInherited ? Visibility.Visible : Visibility.Collapsed;
                    item.IsEnabled = canGoToDefinition;
                }
            }

            foreach (var sep in grid.ContextMenu.Items.OfType<Separator>().Where(s => Equals(s.Tag, "kv-clipboard-sep")).ToList())
            {
                sep.Visibility = showRowClipboard ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        catch
        {
            // Never let clipboard menu wiring crash the grid right-click.
        }
    }

    private static bool IsInheritedVariablesGrid(DataGrid grid) =>
        grid.DataContext is ContainerNodeViewModel container
        && ReferenceEquals(UnwrapItemsSource(grid.ItemsSource), container.InheritedVariables);

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

    private static void CopyField(DataGrid grid, bool copyValue)
    {
        if (!TryGetPrimaryNameValue(grid, out var name, out var value))
        {
            return;
        }

        try
        {
            Clipboard.SetText(copyValue ? value : name);
        }
        catch
        {
            // Clipboard can be locked by another process.
        }
    }

    private static void GoToDefinition(DataGrid grid)
    {
        if (!TryGetInheritedSelection(grid, out var inherited)
            || Application.Current?.MainWindow?.DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.GoToVariableDefinition(inherited);
    }

    private static bool TryGetInheritedSelection(DataGrid grid, out InheritedVariableRow row)
    {
        row = null!;
        if (grid.SelectedItem is InheritedVariableRow selected)
        {
            row = selected;
            return true;
        }

        if (grid.CurrentItem is InheritedVariableRow current)
        {
            row = current;
            return true;
        }

        return false;
    }

    private static bool TryGetPrimaryNameValue(DataGrid grid, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;

        foreach (var row in KeyValueGridSelection.SnapshotSelectedRows(grid))
        {
            name = row.Key ?? string.Empty;
            value = row.Value ?? string.Empty;
            return true;
        }

        switch (grid.CurrentItem ?? grid.SelectedItem)
        {
            case KeyValueItemViewModel kv:
                name = kv.Key ?? string.Empty;
                value = kv.Value ?? string.Empty;
                return true;
            case InheritedVariableRow inherited:
                name = inherited.Key ?? string.Empty;
                value = inherited.Value ?? string.Empty;
                return true;
            default:
                return false;
        }
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
