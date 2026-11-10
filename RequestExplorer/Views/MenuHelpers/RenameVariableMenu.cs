using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Views.MenuHelpers;

/// <summary>
/// Experimental. Adds Rename (Folder/Collection/Global) on local Variables grids only.
/// </summary>
public static class RenameVariableMenu
{
    public const string MenuTag = "rename-variable";
    public const string CollectionMenuTag = "rename-variable-collection";
    public const string GlobalMenuTag = "rename-variable-global";

    public static void SyncItems(DataGrid grid, ContextMenu menu)
    {
        ClearItems(menu);

        if (!IsFeatureEnabled(out var main) || main is null)
        {
            return;
        }

        if (!TryGetTarget(grid, out var container, out var item))
        {
            return;
        }

        var capturedContainer = container;
        var capturedItem = item;
        var insertAt = InsertIndex(menu);
        if (insertAt > 0 && menu.Items[insertAt - 1] is not Separator)
        {
            menu.Items.Insert(insertAt, new Separator { Tag = MenuTag });
            insertAt++;
        }

        var renameItem = new MenuItem
        {
            Header = "Rename (Folder)…",
            Tag = MenuTag,
            IsEnabled = item.HasKey
        };
        renameItem.Click += (_, _) => main.RenameVariable(capturedContainer, capturedItem);
        menu.Items.Insert(insertAt++, renameItem);

        if (main.UiPreferences.EnableRenameVariableCollection)
        {
            var collectionItem = new MenuItem
            {
                Header = "Rename (Collection)…",
                Tag = CollectionMenuTag,
                IsEnabled = item.HasKey
            };
            collectionItem.Click += (_, _) =>
                main.RenameVariableCollection(capturedContainer, capturedItem);
            menu.Items.Insert(insertAt++, collectionItem);
        }

        if (main.UiPreferences.EnableRenameVariableGlobal)
        {
            var globalItem = new MenuItem
            {
                Header = "Rename (Global)…",
                Tag = GlobalMenuTag,
                IsEnabled = item.HasKey
            };
            globalItem.Click += (_, _) =>
                main.RenameVariableGlobal(capturedContainer, capturedItem);
            menu.Items.Insert(insertAt, globalItem);
        }
    }

    private static bool IsFeatureEnabled(out MainViewModel? main)
    {
        main = Application.Current?.MainWindow?.DataContext as MainViewModel;
        return main?.UiPreferences.EnableRenameVariable == true;
    }

    private static bool TryGetTarget(
        DataGrid grid,
        out ContainerNodeViewModel container,
        out KeyValueItemViewModel item)
    {
        container = null!;
        item = null!;

        if (grid.DataContext is not ContainerNodeViewModel context)
        {
            return false;
        }

        // Local Variables only — inherited rows use Go to Definition / double-click first.
        var source = UnwrapItemsSource(grid.ItemsSource);
        if (!ReferenceEquals(source, context.Variables))
        {
            return false;
        }

        var local = GetLocalRow(grid);
        if (local is null || !local.HasKey)
        {
            return false;
        }

        container = context;
        item = local;
        return true;
    }

    private static KeyValueItemViewModel? GetLocalRow(DataGrid grid)
    {
        if (grid.SelectedItem is KeyValueItemViewModel selected && selected.HasKey)
        {
            return selected;
        }

        return grid.CurrentItem is KeyValueItemViewModel current && current.HasKey
            ? current
            : null;
    }

    private static object? UnwrapItemsSource(object? itemsSource) =>
        itemsSource is ICollectionView view ? view.SourceCollection : itemsSource;

    private static void ClearItems(ContextMenu menu)
    {
        for (var i = menu.Items.Count - 1; i >= 0; i--)
        {
            switch (menu.Items[i])
            {
                case MenuItem { Tag: string tag }
                    when tag is MenuTag or CollectionMenuTag or GlobalMenuTag:
                case Separator { Tag: string sepTag } when sepTag == MenuTag:
                    menu.Items.RemoveAt(i);
                    break;
            }
        }
    }

    private static int InsertIndex(ContextMenu menu)
    {
        var afterApply = -1;
        var afterCopyValue = -1;
        for (var i = 0; i < menu.Items.Count; i++)
        {
            if (menu.Items[i] is MenuItem { Tag: string tag })
            {
                if (tag == ApplyVariableToUrlsMenu.MenuTag)
                {
                    afterApply = i;
                }
                else if (tag == "kv-copy-value")
                {
                    afterCopyValue = i;
                }
            }
            else if (menu.Items[i] is Separator { Tag: string sepTag }
                     && sepTag == ApplyVariableToUrlsMenu.MenuTag)
            {
                afterApply = i;
            }
        }

        if (afterApply >= 0)
        {
            return afterApply + 1;
        }

        if (afterCopyValue >= 0)
        {
            return afterCopyValue + 1;
        }

        return 0;
    }
}
