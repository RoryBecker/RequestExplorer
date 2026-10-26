using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RequestExplorer.Services.Variables;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Views.MenuHelpers;

/// <summary>
/// Adds "Apply Variable to URLs…" on collection/folder Variables grids (local and inherited).
/// </summary>
public static class ApplyVariableToUrlsMenu
{
    public const string MenuTag = "apply-variable-to-urls";

    public static void SyncItems(DataGrid grid, ContextMenu menu)
    {
        ClearItems(menu);

        if (grid.DataContext is not ContainerNodeViewModel context)
        {
            return;
        }

        var source = UnwrapItemsSource(grid.ItemsSource);
        KeyValueItemViewModel? definition = null;
        if (ReferenceEquals(source, context.Variables))
        {
            definition = GetLocalRow(grid);
        }
        else if (ReferenceEquals(source, context.InheritedVariables)
                 && GetInheritedRow(grid) is { } inherited)
        {
            definition = inherited.SourceItem;
        }

        if (definition is null || !definition.HasKey)
        {
            return;
        }

        var capturedContext = context;
        var capturedDefinition = definition;
        var canApply = definition.IsEnabled
                       && UrlVariableTreeApplicator.FindAffectedRequests(context, definition).Count > 0;

        var insertAt = InsertIndex(menu);
        if (insertAt > 0 && menu.Items[insertAt - 1] is not Separator)
        {
            menu.Items.Insert(insertAt, new Separator { Tag = MenuTag });
            insertAt++;
        }

        var item = new MenuItem
        {
            Header = "Apply Variable to URLs…",
            Tag = MenuTag,
            IsEnabled = canApply
        };
        item.Click += (_, _) => Apply(capturedContext, capturedDefinition);
        menu.Items.Insert(insertAt, item);
    }

    private static void Apply(ContainerNodeViewModel context, KeyValueItemViewModel definition)
    {
        if (Application.Current?.MainWindow?.DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.ApplyVariableToUrls(context, definition);
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

    private static InheritedVariableRow? GetInheritedRow(DataGrid grid)
    {
        if (grid.SelectedItem is InheritedVariableRow selected)
        {
            return selected;
        }

        return grid.CurrentItem as InheritedVariableRow;
    }

    private static object? UnwrapItemsSource(object? itemsSource) =>
        itemsSource is ICollectionView view ? view.SourceCollection : itemsSource;

    private static void ClearItems(ContextMenu menu)
    {
        for (var i = menu.Items.Count - 1; i >= 0; i--)
        {
            switch (menu.Items[i])
            {
                case MenuItem { Tag: string tag } when tag == MenuTag:
                case Separator { Tag: string sepTag } when sepTag == MenuTag:
                    menu.Items.RemoveAt(i);
                    break;
            }
        }
    }

    private static int InsertIndex(ContextMenu menu)
    {
        var lastGoto = -1;
        var afterCopyValue = -1;
        for (var i = 0; i < menu.Items.Count; i++)
        {
            if (menu.Items[i] is MenuItem { Tag: string tag })
            {
                if (tag == VariableGoToDefinitionMenu.MenuTag)
                {
                    lastGoto = i;
                }
                else if (tag == "kv-copy-value")
                {
                    afterCopyValue = i;
                }
            }
            else if (menu.Items[i] is Separator { Tag: string sepTag }
                     && sepTag == VariableGoToDefinitionMenu.MenuTag)
            {
                lastGoto = i;
            }
        }

        if (lastGoto >= 0)
        {
            return lastGoto + 1;
        }

        if (afterCopyValue >= 0)
        {
            return afterCopyValue + 1;
        }

        return 0;
    }
}
