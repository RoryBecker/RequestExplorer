using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RequestExplorer.Services.Undo;
using RequestExplorer.Services.Variables;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;
using RequestExplorer.Views.GridHelpers;

namespace RequestExplorer.Views.MenuHelpers;

/// <summary>
/// Context-menu Move actions that relocate Params/Headers/Variables rows to named container targets.
/// </summary>
public static class KeyValueGridVariableMenus
{
    private const string MoveMenuTag = "kv-move-root";

    public static readonly DependencyProperty EnableMoveMenusProperty =
        DependencyProperty.RegisterAttached(
            "EnableMoveMenus",
            typeof(bool),
            typeof(KeyValueGridVariableMenus),
            new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnableMoveMenus(DependencyObject element, bool value) =>
        element.SetValue(EnableMoveMenusProperty, value);

    public static bool GetEnableMoveMenus(DependencyObject element) =>
        (bool)element.GetValue(EnableMoveMenusProperty);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        grid.ContextMenuOpening -= OnContextMenuOpening;
        grid.Loaded -= OnLoaded;

        if (e.NewValue is true)
        {
            grid.ContextMenuOpening += OnContextMenuOpening;
            grid.Loaded += OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            grid.ContextMenu ??= new ContextMenu();
        }
    }

    private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        try
        {
            grid.ContextMenu ??= new ContextMenu();
            ClearMoveItems(grid.ContextMenu);

            var item = GetPrimaryRow(grid);
            if (item is null || !item.HasKey)
            {
                return;
            }

            if (grid.DataContext is RequestNodeViewModel request
                && TryGetRequestListKind(grid, request, out var listKind))
            {
                AddRequestMoveMenus(grid.ContextMenu, request, listKind, item);
                return;
            }

            if (grid.DataContext is ContainerNodeViewModel container
                && ReferenceEquals(UnwrapItemsSource(grid.ItemsSource), container.Variables))
            {
                AddVariableMoveMenus(grid.ContextMenu, container, item);
            }
        }
        catch
        {
            // Never let move-menu wiring crash the grid right-click.
            try
            {
                if (grid.ContextMenu is not null)
                {
                    ClearMoveItems(grid.ContextMenu);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private static void AddRequestMoveMenus(
        ContextMenu menu,
        RequestNodeViewModel request,
        KeyValueListKind listKind,
        KeyValueItemViewModel item)
    {
        var targets = VariableScopeMove.EnumerateAncestorContainers(request).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        EnsureSeparator(menu);
        var root = new MenuItem { Header = "Move to variable on…", Tag = MoveMenuTag };
        foreach (var target in targets)
        {
            var captured = target;
            var kindLabel = target is CollectionNodeViewModel ? "collection" : "folder";
            var entry = new MenuItem { Header = $"{kindLabel} '{target.Name}'" };
            entry.Click += (_, _) =>
            {
                VariableScopeMove.TryMoveRequestRowToVariable(
                    request,
                    listKind,
                    item,
                    captured,
                    GetUndoRecorder(request),
                    ConfirmOverwrite);
            };
            root.Items.Add(entry);
        }

        menu.Items.Add(root);
    }

    private static void AddVariableMoveMenus(
        ContextMenu menu,
        ContainerNodeViewModel source,
        KeyValueItemViewModel item)
    {
        var up = VariableScopeMove.EnumerateAncestorContainers(source).ToList();
        var down = VariableScopeMove.EnumerateDirectChildFolders(source).ToList();
        if (up.Count == 0 && down.Count == 0)
        {
            return;
        }

        EnsureSeparator(menu);

        if (up.Count > 0)
        {
            var upRoot = new MenuItem { Header = "Move up to…", Tag = MoveMenuTag };
            foreach (var target in up)
            {
                var captured = target;
                var kindLabel = target is CollectionNodeViewModel ? "collection" : "folder";
                var entry = new MenuItem { Header = $"{kindLabel} '{target.Name}'" };
                entry.Click += (_, _) =>
                {
                    VariableScopeMove.TryMoveVariableToContainer(
                        source,
                        item,
                        captured,
                        source.UndoRecorder,
                        ConfirmOverwrite);
                };
                upRoot.Items.Add(entry);
            }

            menu.Items.Add(upRoot);
        }

        if (down.Count > 0)
        {
            var downRoot = new MenuItem { Header = "Move down to…", Tag = MoveMenuTag };
            foreach (var target in down)
            {
                var captured = target;
                var entry = new MenuItem { Header = $"folder '{target.Name}'" };
                entry.Click += (_, _) =>
                {
                    VariableScopeMove.TryMoveVariableToContainer(
                        source,
                        item,
                        captured,
                        source.UndoRecorder,
                        ConfirmOverwrite);
                };
                downRoot.Items.Add(entry);
            }

            menu.Items.Add(downRoot);
        }
    }

    private static bool ConfirmOverwrite(string key, string targetName, string existingValue, string incomingValue)
    {
        var message =
            $"A variable named '{key}' already exists on '{targetName}'.\n\n"
            + $"Current value:\n{FormatValue(existingValue)}\n\n"
            + $"New value:\n{FormatValue(incomingValue)}\n\n"
            + "Overwrite the existing variable?";

        return ConfirmDialog.Confirm(
            Application.Current?.MainWindow,
            "Variable already exists",
            message,
            yesText: "Overwrite",
            noText: "Cancel");
    }

    private static string FormatValue(string value) =>
        string.IsNullOrEmpty(value) ? "(empty)" : value;

    private static IUndoRecorder? GetUndoRecorder(RequestNodeViewModel request)
    {
        // Walk to a container that was given the shared recorder, or use MainViewModel via window.
        if (Application.Current?.MainWindow?.DataContext is MainViewModel main)
        {
            return main;
        }

        for (var node = request.Parent; node is not null; node = node.Parent)
        {
            if (node is ContainerNodeViewModel { UndoRecorder: { } recorder })
            {
                return recorder;
            }
        }

        return null;
    }

    private static KeyValueItemViewModel? GetPrimaryRow(DataGrid grid)
    {
        var selected = KeyValueGridSelection.SnapshotSelectedRows(grid)
            .FirstOrDefault(i => i.HasKey);
        if (selected is not null)
        {
            return selected;
        }

        return grid.CurrentItem is KeyValueItemViewModel current && current.HasKey
            ? current
            : null;
    }

    private static bool TryGetRequestListKind(
        DataGrid grid,
        RequestNodeViewModel request,
        out KeyValueListKind listKind)
    {
        var source = UnwrapItemsSource(grid.ItemsSource);
        if (ReferenceEquals(source, request.Params))
        {
            listKind = KeyValueListKind.Params;
            return true;
        }

        if (ReferenceEquals(source, request.Headers))
        {
            listKind = KeyValueListKind.Headers;
            return true;
        }

        listKind = default;
        return false;
    }

    private static object? UnwrapItemsSource(object? itemsSource) =>
        itemsSource is ICollectionView view ? view.SourceCollection : itemsSource;

    private static void ClearMoveItems(ContextMenu menu)
    {
        // Single reverse pass: removing a move root and its separator in one iteration
        // can leave i past Count on the next step (ArgumentOutOfRangeException).
        for (var i = menu.Items.Count - 1; i >= 0; i--)
        {
            switch (menu.Items[i])
            {
                case MenuItem { Tag: string tag } when tag == MoveMenuTag:
                case Separator { Tag: string sepTag } when sepTag == MoveMenuTag:
                    menu.Items.RemoveAt(i);
                    break;
            }
        }
    }

    private static void EnsureSeparator(ContextMenu menu)
    {
        if (menu.Items.Count == 0)
        {
            return;
        }

        // Only reuse a separator we own — never retag clipboard/other separators.
        if (menu.Items[^1] is Separator { Tag: string tag } && tag == MoveMenuTag)
        {
            return;
        }

        menu.Items.Add(new Separator { Tag = MoveMenuTag });
    }
}
