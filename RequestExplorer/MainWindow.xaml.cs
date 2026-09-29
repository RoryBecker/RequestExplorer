using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RequestExplorer.ViewModels;
using RequestExplorer.Views;

namespace RequestExplorer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Persist in-progress title edits before command keybindings (e.g. Ctrl+S) run.
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            TitleEditHelper.CommitIfFocusedTitle(
                RequestEditor.NameTitleBox,
                CollectionEditor.NameTitleBox,
                FolderEditor.NameTitleBox);
        }
    }

    private void UndoHistoryDrop_OnClick(object sender, RoutedEventArgs e)
    {
        OpenHistoryMenu(sender, UndoHistoryMenu, e);
    }

    private void RedoHistoryDrop_OnClick(object sender, RoutedEventArgs e)
    {
        OpenHistoryMenu(sender, RedoHistoryMenu, e);
    }

    private void OpenHistoryMenu(object sender, ContextMenu? menu, RoutedEventArgs e)
    {
        if (sender is not Button button || menu is null)
        {
            return;
        }

        menu.DataContext = DataContext;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void CollectionTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SelectedNode = e.NewValue as TreeNodeViewModel;
        }
    }

    private void CollectionTree_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var treeItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (treeItem?.DataContext is not TreeNodeViewModel node)
        {
            return;
        }

        treeItem.Focus();
        treeItem.IsSelected = true;

        if (DataContext is MainViewModel vm)
        {
            vm.SelectedNode = node;
        }
    }

    private void TreeViewItem_OnPreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem { DataContext: TreeNodeViewModel node }
            || DataContext is not MainViewModel vm)
        {
            return;
        }

        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        e.Handled = true;
        vm.SelectedNode = node;
        Dispatcher.BeginInvoke(FocusSelectedTitle, DispatcherPriority.Input);
    }

    private void FocusSelectedTitle()
    {
        TextBox? titleBox = null;
        if (DataContext is MainViewModel { IsRequestSelected: true })
        {
            titleBox = RequestEditor.NameTitleBox;
        }
        else if (DataContext is MainViewModel { IsCollectionSelected: true })
        {
            titleBox = CollectionEditor.NameTitleBox;
        }
        else if (DataContext is MainViewModel { IsFolderSelected: true })
        {
            titleBox = FolderEditor.NameTitleBox;
        }

        TitleEditHelper.FocusAndSelect(titleBox);
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

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.ConfirmClose())
        {
            e.Cancel = true;
        }
    }

    private void TreeAddButton_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button button || DataContext is not MainViewModel vm)
        {
            return;
        }

        if (button.DataContext is not TreeNodeViewModel node
            || node is not (CollectionNodeViewModel or FolderNodeViewModel))
        {
            return;
        }

        vm.SelectedNode = node;

        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom
        };

        menu.Items.Add(new MenuItem
        {
            Header = "New Folder",
            Command = vm.AddFolderCommand
        });
        menu.Items.Add(new MenuItem
        {
            Header = "New Request",
            Command = vm.AddRequestCommand
        });

        button.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void TreeDeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button button
            || button.DataContext is not TreeNodeViewModel node
            || DataContext is not MainViewModel vm)
        {
            return;
        }

        if (vm.DeleteNodeCommand.CanExecute(node))
        {
            vm.DeleteNodeCommand.Execute(node);
        }
    }

    private void TreeMoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button button
            || button.DataContext is not TreeNodeViewModel node
            || DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.SelectedNode = node;

        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom
        };

        menu.Items.Add(new MenuItem
        {
            Header = "Copy",
            Command = vm.CopyNodeCommand,
            CommandParameter = node,
            InputGestureText = "Ctrl+C"
        });

        if (node is CollectionNodeViewModel or FolderNodeViewModel)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "Paste",
                Command = vm.PasteIntoNodeCommand,
                CommandParameter = node
            });
        }

        menu.Items.Add(new MenuItem
        {
            Header = "Paste at Top Level",
            Command = vm.PasteAtRootCommand
        });

        menu.Items.Add(new Separator());

        menu.Items.Add(new MenuItem
        {
            Header = "Export for Postman…",
            Command = vm.ExportNodeCommand,
            CommandParameter = node
        });

        if (node is CollectionNodeViewModel or FolderNodeViewModel)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "Import Postman Collection…",
                Command = vm.ImportIntoNodeCommand,
                CommandParameter = node
            });
        }

        button.ContextMenu = menu;
        menu.IsOpen = true;
    }
}
