using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RequestExplorer.ViewModels;
using RequestExplorer.Views;

namespace RequestExplorer;

public partial class MainWindow : Window
{
    private const double QuickExportDefaultWidth = 320;
    private const double QuickExportMinWidth = 220;
    private const double CollectionsDefaultWidth = 360;
    private const double CollectionsMinWidth = 220;

    private readonly TreeDragDropController _treeDragDrop;
    private MainViewModel? _subscribedVm;
    private double _quickExportWidth = QuickExportDefaultWidth;
    private bool _uiPreferencesApplied;
    private TreeNodeViewModel? _pendingMultiSelectClearNode;

    public MainWindow()
    {
        // Subscribe before InitializeComponent so the XAML DataContext assignment is observed.
        DataContextChanged += MainWindow_OnDataContextChanged;
        InitializeComponent();
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
        PreviewKeyUp += MainWindow_OnPreviewKeyUp;
        Loaded += MainWindow_OnLoaded;
        SizeChanged += (_, _) => CaptureWindowBounds();
        LocationChanged += (_, _) => CaptureWindowBounds();
        StateChanged += (_, _) => CaptureWindowBounds();
        _treeDragDrop = new TreeDragDropController(CollectionTree, () => DataContext as MainViewModel);
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyUiPreferences();
        SyncQuickExportColumns();
        if (DataContext is MainViewModel vm)
        {
            // Phase E (C2): backup after UI is shown, not before first paint.
            Dispatcher.BeginInvoke(
                vm.StartStartupBackupIfNeeded,
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    private void MainWindow_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= ViewModel_OnPropertyChanged;
            _subscribedVm.TreeRevealRequested -= ViewModel_OnTreeRevealRequested;
            _subscribedVm.PendingEditCommitRequested -= ViewModel_OnPendingEditCommitRequested;
            _subscribedVm = null;
        }

        if (e.NewValue is MainViewModel vm)
        {
            _subscribedVm = vm;
            _subscribedVm.PropertyChanged += ViewModel_OnPropertyChanged;
            _subscribedVm.TreeRevealRequested += ViewModel_OnTreeRevealRequested;
            _subscribedVm.PendingEditCommitRequested += ViewModel_OnPendingEditCommitRequested;
            _quickExportWidth = SanitizeQuickExportWidth(vm.UiPreferences.QuickExportWidth);
            if (IsLoaded)
            {
                ApplyUiPreferences();
                SyncQuickExportColumns();
            }
        }
    }

    private void ViewModel_OnPendingEditCommitRequested()
    {
        EditorCommitHelper.CommitPendingEdits(
            RequestEditor.NameTitleBox,
            CollectionEditor.NameTitleBox,
            FolderEditor.NameTitleBox);
    }

    private void ViewModel_OnTreeRevealRequested(IReadOnlyList<TreeNodeViewModel> nodes)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        // Wait for expand/filter layout before scrolling the restored selection into view.
        Dispatcher.BeginInvoke(() =>
        {
            CollectionTree.UpdateLayout();
            var primary = nodes[0];
            if (TryFocusTreeNode(primary))
            {
                return;
            }

            // Containers for newly re-parented nodes may need a second layout pass.
            Dispatcher.BeginInvoke(() => TryFocusTreeNode(primary),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsQuickExportVisible))
        {
            SyncQuickExportColumns();
            CaptureAndSaveUiPreferences();
        }
    }

    private void ApplyUiPreferences()
    {
        if (_subscribedVm is null)
        {
            return;
        }

        var prefs = _subscribedVm.UiPreferences;
        _quickExportWidth = SanitizeQuickExportWidth(prefs.QuickExportWidth);

        var collectionsWidth = prefs.CollectionsWidth;
        if (collectionsWidth < CollectionsMinWidth)
        {
            collectionsWidth = CollectionsDefaultWidth;
        }

        CollectionsColumn.Width = new GridLength(collectionsWidth);

        // Restore as star ratios so the two panes always fill the RHS height.
        // Absolute pixel heights leave a dead band when the window is taller than the sum.
        if (prefs.RequestPaneHeight >= 220 && prefs.ResponsePaneHeight >= 160)
        {
            var total = prefs.RequestPaneHeight + prefs.ResponsePaneHeight;
            RequestRow.Height = new GridLength(prefs.RequestPaneHeight / total, GridUnitType.Star);
            ResponseRow.Height = new GridLength(prefs.ResponsePaneHeight / total, GridUnitType.Star);
        }

        if (!_uiPreferencesApplied)
        {
            if (prefs.WindowWidth >= MinWidth && prefs.WindowHeight >= MinHeight)
            {
                Width = prefs.WindowWidth;
                Height = prefs.WindowHeight;
            }

            if (!double.IsNaN(prefs.WindowLeft)
                && !double.IsNaN(prefs.WindowTop)
                && IsWindowOnScreen(prefs.WindowLeft, prefs.WindowTop, Width, Height))
            {
                Left = prefs.WindowLeft;
                Top = prefs.WindowTop;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }

            if (Enum.TryParse<WindowState>(prefs.WindowState, ignoreCase: true, out var state)
                && state is WindowState.Normal or WindowState.Maximized)
            {
                WindowState = state;
            }

            _uiPreferencesApplied = true;
        }
    }

    private void SyncQuickExportColumns()
    {
        var visible = _subscribedVm?.IsQuickExportVisible == true;
        if (visible)
        {
            if (_quickExportWidth < QuickExportMinWidth)
            {
                _quickExportWidth = QuickExportDefaultWidth;
            }

            QuickExportSplitterColumn.Width = new GridLength(6);
            QuickExportColumn.MinWidth = QuickExportMinWidth;
            QuickExportColumn.Width = new GridLength(_quickExportWidth);
        }
        else
        {
            if (QuickExportColumn.Width.IsAbsolute && QuickExportColumn.Width.Value > 0)
            {
                _quickExportWidth = QuickExportColumn.Width.Value;
            }

            QuickExportSplitterColumn.Width = new GridLength(0);
            QuickExportColumn.MinWidth = 0;
            QuickExportColumn.Width = new GridLength(0);
        }
    }

    private void PanelSplitter_OnDragCompleted(object sender, DragCompletedEventArgs e) =>
        CaptureAndSaveUiPreferences();

    private void CaptureAndSaveUiPreferences()
    {
        if (_subscribedVm is null)
        {
            return;
        }

        var prefs = _subscribedVm.UiPreferences;

        if (CollectionsColumn.Width.IsAbsolute && CollectionsColumn.Width.Value > 0)
        {
            prefs.CollectionsWidth = CollectionsColumn.Width.Value;
        }
        else if (CollectionsColumn.ActualWidth > 0)
        {
            prefs.CollectionsWidth = CollectionsColumn.ActualWidth;
        }

        if (QuickExportColumn.Width.IsAbsolute && QuickExportColumn.Width.Value > 0)
        {
            _quickExportWidth = QuickExportColumn.Width.Value;
        }

        prefs.QuickExportWidth = SanitizeQuickExportWidth(_quickExportWidth);

        if (RequestRow.ActualHeight >= 220 && ResponseRow.ActualHeight >= 160)
        {
            prefs.RequestPaneHeight = RequestRow.ActualHeight;
            prefs.ResponsePaneHeight = ResponseRow.ActualHeight;
        }

        CaptureWindowBounds();
        _subscribedVm.PersistUiPreferences();
    }

    private void CaptureWindowBounds()
    {
        if (_subscribedVm is null || !_uiPreferencesApplied)
        {
            return;
        }

        var prefs = _subscribedVm.UiPreferences;
        prefs.WindowState = WindowState.ToString();

        if (WindowState == WindowState.Normal)
        {
            prefs.WindowWidth = Width;
            prefs.WindowHeight = Height;
            prefs.WindowLeft = Left;
            prefs.WindowTop = Top;
        }
        else if (RestoreBounds is { Width: > 0, Height: > 0 } bounds)
        {
            prefs.WindowWidth = bounds.Width;
            prefs.WindowHeight = bounds.Height;
            prefs.WindowLeft = bounds.Left;
            prefs.WindowTop = bounds.Top;
        }
    }

    private static double SanitizeQuickExportWidth(double width) =>
        width < QuickExportMinWidth || width > 720 ? QuickExportDefaultWidth : width;

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

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Commit in-progress title / grid / text edits before Save keybindings run.
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            EditorCommitHelper.CommitPendingEdits(
                RequestEditor.NameTitleBox,
                CollectionEditor.NameTitleBox,
                FolderEditor.NameTitleBox);
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        if (EscapeCancel.CloseAnyOpenContextMenu(this))
        {
            e.Handled = true;
            return;
        }

        if (_treeDragDrop.CancelPendingOrIndicators())
        {
            e.Handled = true;
        }
    }

    private void MainWindow_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.LeftAlt or Key.RightAlt or Key.System))
        {
            return;
        }

        if (DataContext is MainViewModel vm)
        {
            vm.EndAltMoveFilterDeferral();
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
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        // Ctrl/Shift selection is handled in PreviewMouseLeftButtonDown.
        if (Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Shift
            or (ModifierKeys.Control | ModifierKeys.Shift))
        {
            return;
        }

        if (e.NewValue is TreeNodeViewModel node)
        {
            vm.SelectedNode = node;
        }
    }

    private void CollectionTree_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pendingMultiSelectClearNode = null;
        if (DataContext is not MainViewModel vm
            || FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not TreeNodeViewModel node)
        {
            return;
        }

        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Shift))
        {
            vm.SelectRange(node);
            item.Focus();
            e.Handled = true;
            return;
        }

        if (mods.HasFlag(ModifierKeys.Control))
        {
            vm.ToggleSelection(node);
            item.Focus();
            e.Handled = true;
            return;
        }

        if (vm.IsNodeInSelection(node) && vm.SelectedNodes.Count > 1)
        {
            // Defer collapsing multi-select until mouse up if this was not a drag.
            _pendingMultiSelectClearNode = node;
            return;
        }

        vm.SelectOnly(node);
    }

    private void CollectionTree_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingMultiSelectClearNode is null
            || DataContext is not MainViewModel vm
            || _treeDragDrop.DidCompleteDrag)
        {
            _pendingMultiSelectClearNode = null;
            _treeDragDrop.ResetDidCompleteDrag();
            return;
        }

        if (!_treeDragDrop.IsDragActive)
        {
            vm.SelectOnly(_pendingMultiSelectClearNode);
        }

        _pendingMultiSelectClearNode = null;
        _treeDragDrop.ResetDidCompleteDrag();
    }

    private void TreeFilterOptionsButton_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.DataContext = DataContext;
        button.ContextMenu.IsOpen = true;
    }

    private void TreeFilterBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var target = vm.SelectedNode is { IsVisibleInFilter: true } selected
            ? selected
            : GetFirstVisibleNode(vm);

        if (target is null || !TryFocusTreeNode(target))
        {
            return;
        }

        e.Handled = true;
    }

    private void CollectionTree_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is ButtonBase)
        {
            return;
        }

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Control)
        {
            vm.CollapseAllNodesCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up
            && Keyboard.Modifiers == ModifierKeys.None
            && vm.SelectedNode is not null
            && ReferenceEquals(vm.SelectedNode, GetFirstVisibleNode(vm)))
        {
            TreeFilterBox.Focus();
            TreeFilterBox.CaretIndex = TreeFilterBox.Text?.Length ?? 0;
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Enter or Key.Return)
            || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (vm.SelectedNode is not { } node)
        {
            return;
        }

        if (node is not (CollectionNodeViewModel or FolderNodeViewModel)
            || node.Children.Count == 0)
        {
            return;
        }

        node.IsExpanded = !node.IsExpanded;
        e.Handled = true;
    }

    private static TreeNodeViewModel? GetFirstVisibleNode(MainViewModel vm)
    {
        foreach (var collection in vm.Collections)
        {
            if (collection.IsVisibleInFilter)
            {
                return collection;
            }
        }

        return null;
    }

    private bool TryFocusTreeNode(TreeNodeViewModel node)
    {
        CollectionTree.UpdateLayout();
        var item = FindTreeViewItem(CollectionTree, node);
        if (item is null)
        {
            return false;
        }

        item.IsSelected = true;
        item.BringIntoView();
        item.Focus();

        if (DataContext is MainViewModel vm)
        {
            vm.SelectedNode = node;
        }

        return true;
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
        {
            return direct;
        }

        foreach (var child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is not TreeViewItem childItem)
            {
                continue;
            }

            if (childItem.DataContext is TreeNodeViewModel { IsExpanded: false } node
                && node.Children.Count > 0
                && ContainsDescendant(node, item))
            {
                childItem.IsExpanded = true;
                childItem.UpdateLayout();
            }

            var nested = FindTreeViewItem(childItem, item);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool ContainsDescendant(TreeNodeViewModel root, object item)
    {
        foreach (var child in root.Children)
        {
            if (ReferenceEquals(child, item) || ContainsDescendant(child, item))
            {
                return true;
            }
        }

        return false;
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
        if (sender is not TreeViewItem item
            || item.DataContext is not TreeNodeViewModel node
            || DataContext is not MainViewModel vm)
        {
            return;
        }

        // PreviewMouseDoubleClick also fires on ancestor TreeViewItems; only handle the clicked node.
        var clickedItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (!ReferenceEquals(clickedItem, item))
        {
            return;
        }

        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        e.Handled = true;
        vm.SelectedNode = node;

        if (node is CollectionNodeViewModel or FolderNodeViewModel
            && node.Children.Count > 0)
        {
            node.IsExpanded = !node.IsExpanded;
        }
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
            return;
        }

        CaptureAndSaveUiPreferences();
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
            Header = "Duplicate",
            Command = vm.DuplicateNodeCommand,
            InputGestureText = "Ctrl+D"
        });

        menu.Items.Add(new Separator());

        menu.Items.Add(new MenuItem
        {
            Header = "Copy",
            Command = vm.CopyNodeCommand,
            CommandParameter = node,
            InputGestureText = "Ctrl+C"
        });

        menu.Items.Add(new MenuItem
        {
            Header = "Paste",
            Command = vm.SmartPasteCommand,
            InputGestureText = "Ctrl+V"
        });

        menu.Items.Add(new MenuItem
        {
            Header = "Paste at Top Level",
            Command = vm.PasteAtRootCommand
        });

        menu.Items.Add(new Separator());

        menu.Items.Add(new MenuItem
        {
            Header = vm.FormatRunAllRequestsMenuHeader(node),
            Command = vm.RunAllRequestsCommand,
            CommandParameter = node
        });

        if (node is CollectionNodeViewModel or FolderNodeViewModel)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = "Expand All Children",
                Command = vm.ExpandChildrenCommand,
                CommandParameter = node
            });
            menu.Items.Add(new MenuItem
            {
                Header = "Collapse All Children",
                Command = vm.CollapseChildrenCommand,
                CommandParameter = node
            });
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(new MenuItem
        {
            Header = "Move Up",
            Command = vm.MoveNodeUpCommand,
            InputGestureText = "Alt+Up"
        });
        menu.Items.Add(new MenuItem
        {
            Header = "Move Down",
            Command = vm.MoveNodeDownCommand,
            InputGestureText = "Alt+Down"
        });
        menu.Items.Add(new Separator());

        menu.Items.Add(new MenuItem
        {
            Header = "Delete",
            Command = vm.DeleteNodeCommand,
            CommandParameter = node,
            InputGestureText = "Del"
        });

        menu.Items.Add(new Separator());

        menu.Items.Add(new MenuItem
        {
            Header = "Export for Postman…",
            Command = vm.ExportNodeCommand,
            CommandParameter = node
        });
        menu.Items.Add(new MenuItem
        {
            Header = "Export to Postman (Flatten)…",
            Command = vm.ExportNodeFlattenedCommand,
            CommandParameter = node
        });

        if (node is CollectionNodeViewModel or FolderNodeViewModel)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "Import Postman Collection(s)…",
                Command = vm.ImportIntoNodeCommand,
                CommandParameter = node
            });
        }

        button.ContextMenu = menu;
        menu.IsOpen = true;
    }
}
