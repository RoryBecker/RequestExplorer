using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RequestExplorer.Services;
using RequestExplorer.ViewModels;

namespace RequestExplorer;

/// <summary>Handles tree drag visuals and drop targeting; moves complete only on drop.</summary>
internal sealed class TreeDragDropController
{
    private readonly TreeView _tree;
    private readonly Func<MainViewModel?> _getVm;
    private readonly DispatcherTimer _hoverToggleTimer;

    private Point _dragStart;
    private TreeNodeViewModel? _dragSource;
    private bool _dragging;
    private TreeNodeViewModel? _hoverToggleTarget;
    private TreeNodeViewModel? _currentIndicatorNode;
    private TreeDropPlacement? _currentPlacement;
    private bool _rootIndicator;

    public TreeDragDropController(TreeView tree, Func<MainViewModel?> getVm)
    {
        _tree = tree;
        _getVm = getVm;
        _hoverToggleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _hoverToggleTimer.Tick += HoverToggleTimer_OnTick;

        _tree.AllowDrop = true;
        _tree.PreviewMouseLeftButtonDown += Tree_OnPreviewMouseLeftButtonDown;
        _tree.PreviewMouseMove += Tree_OnPreviewMouseMove;
        _tree.PreviewMouseLeftButtonUp += Tree_OnPreviewMouseLeftButtonUp;
        _tree.DragOver += Tree_OnDragOver;
        _tree.DragLeave += Tree_OnDragLeave;
        _tree.Drop += Tree_OnDrop;
        _tree.GiveFeedback += Tree_OnGiveFeedback;
    }

    private void Tree_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            _dragSource = null;
            return;
        }

        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is TreeNodeViewModel node)
        {
            _dragStart = e.GetPosition(_tree);
            _dragSource = node;
        }
        else
        {
            _dragSource = null;
        }
    }

    private void Tree_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragSource = null;
        _dragging = false;
    }

    private void Tree_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging
            || _dragSource is null
            || e.LeftButton != MouseButtonState.Pressed
            || FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var pos = e.GetPosition(_tree);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragging = true;
        var source = _dragSource;
        try
        {
            var data = new DataObject();
            data.SetData(typeof(TreeNodeViewModel), source);
            data.SetData(source.GetType(), source);
            DragDrop.DoDragDrop(_tree, data, DragDropEffects.Move);
        }
        finally
        {
            _dragging = false;
            _dragSource = null;
            ClearIndicators();
            StopHoverToggle();
        }
    }

    private void Tree_OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = true;
        e.Handled = true;
    }

    private void Tree_OnDragOver(object sender, DragEventArgs e)
    {
        if (!TryGetDragSource(e, out var source))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            ClearIndicators();
            StopHoverToggle();
            return;
        }

        var targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        var position = e.GetPosition(_tree);

        if (targetItem?.DataContext is TreeNodeViewModel target)
        {
            var placement = ResolvePlacement(source, target, targetItem, e.GetPosition(targetItem));
            if (placement is null || !CanDrop(source, target, placement.Value))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                ClearIndicators();
                UpdateHoverToggle(null);
                return;
            }

            ShowIndicator(target, placement.Value);
            UpdateHoverToggle(placement == TreeDropPlacement.Into ? target : null);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        // Empty area / top of tree → root drop
        if (CanDropAtRoot(source) && position.Y < 28)
        {
            ShowRootIndicator();
            UpdateHoverToggle(null);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        // Below last visible item: treat as after last root collection when over tree chrome
        if (_getVm()?.Collections is { Count: > 0 } collections)
        {
            var last = collections[^1];
            if (CanDrop(source, last, TreeDropPlacement.After))
            {
                ShowIndicator(last, TreeDropPlacement.After);
                UpdateHoverToggle(null);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
        ClearIndicators();
        UpdateHoverToggle(null);
    }

    private void Tree_OnDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave fires when entering child elements; only clear when truly leaving the tree.
        var pos = e.GetPosition(_tree);
        if (pos.X < 0 || pos.Y < 0 || pos.X > _tree.ActualWidth || pos.Y > _tree.ActualHeight)
        {
            ClearIndicators();
            StopHoverToggle();
        }
    }

    private void Tree_OnDrop(object sender, DragEventArgs e)
    {
        if (_getVm() is not MainViewModel vm || !TryGetDragSource(e, out var source))
        {
            e.Handled = true;
            ClearIndicators();
            StopHoverToggle();
            return;
        }

        TreeNodeViewModel? target = _currentIndicatorNode;
        TreeDropPlacement? placement = _currentPlacement;
        if (_rootIndicator)
        {
            target = null;
            placement = TreeDropPlacement.Root;
        }

        ClearIndicators();
        StopHoverToggle();

        if (placement is null)
        {
            e.Handled = true;
            return;
        }

        vm.TryMoveNode(source, target, placement.Value);
        e.Handled = true;
    }

    private static bool TryGetDragSource(DragEventArgs e, out TreeNodeViewModel source)
    {
        source = null!;
        if (e.Data.GetDataPresent(typeof(TreeNodeViewModel)))
        {
            source = (TreeNodeViewModel)e.Data.GetData(typeof(TreeNodeViewModel))!;
            return source is not null;
        }

        if (e.Data.GetDataPresent(typeof(CollectionNodeViewModel)))
        {
            source = (TreeNodeViewModel)e.Data.GetData(typeof(CollectionNodeViewModel))!;
            return source is not null;
        }

        if (e.Data.GetDataPresent(typeof(FolderNodeViewModel)))
        {
            source = (TreeNodeViewModel)e.Data.GetData(typeof(FolderNodeViewModel))!;
            return source is not null;
        }

        if (e.Data.GetDataPresent(typeof(RequestNodeViewModel)))
        {
            source = (TreeNodeViewModel)e.Data.GetData(typeof(RequestNodeViewModel))!;
            return source is not null;
        }

        return false;
    }

    private static TreeDropPlacement? ResolvePlacement(
        TreeNodeViewModel source,
        TreeNodeViewModel target,
        TreeViewItem targetItem,
        Point relative)
    {
        var height = Math.Max(targetItem.ActualHeight, 1);
        var ratio = relative.Y / height;

        if (target is CollectionNodeViewModel or FolderNodeViewModel)
        {
            if (ratio < 0.25)
            {
                return TreeDropPlacement.Before;
            }

            if (ratio > 0.75)
            {
                return TreeDropPlacement.After;
            }

            return TreeDropPlacement.Into;
        }

        // Requests: only before/after
        return ratio < 0.5 ? TreeDropPlacement.Before : TreeDropPlacement.After;
    }

    private static bool CanDrop(TreeNodeViewModel source, TreeNodeViewModel target, TreeDropPlacement placement)
    {
        if (ReferenceEquals(source, target) || IsDescendant(target, source))
        {
            return false;
        }

        return placement switch
        {
            TreeDropPlacement.Into =>
                target is CollectionNodeViewModel or FolderNodeViewModel
                && source is not null,
            TreeDropPlacement.Before or TreeDropPlacement.After =>
                target.Parent is CollectionNodeViewModel or FolderNodeViewModel
                || target is CollectionNodeViewModel,
            TreeDropPlacement.Root => CanDropAtRoot(source),
            _ => false
        };
    }

    private static bool CanDropAtRoot(TreeNodeViewModel source) => source is not null;

    private static bool IsDescendant(TreeNodeViewModel candidate, TreeNodeViewModel ancestor)
    {
        var current = candidate;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private void ShowIndicator(TreeNodeViewModel target, TreeDropPlacement placement)
    {
        if (ReferenceEquals(_currentIndicatorNode, target)
            && _currentPlacement == placement
            && !_rootIndicator)
        {
            return;
        }

        ClearIndicators();
        _currentIndicatorNode = target;
        _currentPlacement = placement;
        _rootIndicator = false;
        _getVm()!.IsRootDropIndicatorVisible = false;

        target.DropIndicator = placement switch
        {
            TreeDropPlacement.Before => TreeDropIndicator.Before,
            TreeDropPlacement.After => TreeDropIndicator.After,
            TreeDropPlacement.Into => TreeDropIndicator.Into,
            _ => TreeDropIndicator.None
        };
    }

    private void ShowRootIndicator()
    {
        if (_rootIndicator)
        {
            return;
        }

        ClearIndicators();
        _rootIndicator = true;
        _currentPlacement = TreeDropPlacement.Root;
        if (_getVm() is MainViewModel vm)
        {
            vm.IsRootDropIndicatorVisible = true;
        }
    }

    private void ClearIndicators()
    {
        if (_currentIndicatorNode is not null)
        {
            _currentIndicatorNode.DropIndicator = TreeDropIndicator.None;
            _currentIndicatorNode = null;
        }

        _currentPlacement = null;
        _rootIndicator = false;
        if (_getVm() is MainViewModel vm)
        {
            vm.IsRootDropIndicatorVisible = false;
            vm.ClearAllDropIndicators();
        }
    }

    private void UpdateHoverToggle(TreeNodeViewModel? target)
    {
        if (target is not (CollectionNodeViewModel or FolderNodeViewModel))
        {
            StopHoverToggle();
            return;
        }

        if (ReferenceEquals(_hoverToggleTarget, target))
        {
            return;
        }

        _hoverToggleTarget = target;
        _hoverToggleTimer.Stop();
        _hoverToggleTimer.Start();
    }

    private void StopHoverToggle()
    {
        _hoverToggleTimer.Stop();
        _hoverToggleTarget = null;
    }

    private void HoverToggleTimer_OnTick(object? sender, EventArgs e)
    {
        _hoverToggleTimer.Stop();
        if (_hoverToggleTarget is not null)
        {
            _hoverToggleTarget.IsExpanded = !_hoverToggleTarget.IsExpanded;
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
}
