using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Views.GridHelpers;

/// <summary>
/// Left-side grip to drag rows, plus Alt+Up / Alt+Down, to set natural order
/// of Params, Headers, and Local Variables. The trailing spare row stays last.
/// </summary>
public static class KeyValueGridReorder
{
    private const string DragFormat = "RequestExplorer.KeyValueRowDrag";

    private static readonly DependencyProperty IsGripColumnProperty =
        DependencyProperty.RegisterAttached(
            "IsGripColumn",
            typeof(bool),
            typeof(KeyValueGridReorder));

    private static readonly BooleanToVisibilityConverter BoolToVis = new();
    private static readonly ConditionalWeakTable<DataGrid, DragState> States = new();

    public static readonly DependencyProperty EnableRowReorderProperty =
        DependencyProperty.RegisterAttached(
            "EnableRowReorder",
            typeof(bool),
            typeof(KeyValueGridReorder),
            new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnableRowReorder(DependencyObject element, bool value) =>
        element.SetValue(EnableRowReorderProperty, value);

    public static bool GetEnableRowReorder(DependencyObject element) =>
        (bool)element.GetValue(EnableRowReorderProperty);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        grid.AllowDrop = false;
        grid.PreviewKeyDown -= OnPreviewKeyDown;
        grid.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        grid.PreviewMouseMove -= OnPreviewMouseMove;
        grid.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        grid.CurrentCellChanged -= OnCurrentCellChanged;
        grid.DragOver -= OnDragOver;
        grid.DragLeave -= OnDragLeave;
        grid.Drop -= OnDrop;
        grid.QueryContinueDrag -= OnQueryContinueDrag;
        grid.Loaded -= OnGridLoaded;

        if (e.NewValue is true)
        {
            grid.AllowDrop = true;
            grid.PreviewKeyDown += OnPreviewKeyDown;
            grid.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            grid.PreviewMouseMove += OnPreviewMouseMove;
            grid.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            grid.CurrentCellChanged += OnCurrentCellChanged;
            grid.DragOver += OnDragOver;
            grid.DragLeave += OnDragLeave;
            grid.Drop += OnDrop;
            grid.QueryContinueDrag += OnQueryContinueDrag;
            grid.Loaded += OnGridLoaded;
            EnsureGripColumn(grid);
        }
        else
        {
            RemoveGripColumn(grid);
        }
    }

    private static void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            EnsureGripColumn(grid);
        }
    }

    private static void EnsureGripColumn(DataGrid grid)
    {
        if (grid.Columns.Any(IsGripColumn))
        {
            return;
        }

        grid.Columns.Insert(0, CreateGripColumn());
    }

    private static void RemoveGripColumn(DataGrid grid)
    {
        var column = grid.Columns.FirstOrDefault(IsGripColumn);
        if (column is not null)
        {
            grid.Columns.Remove(column);
        }
    }

    private static DataGridTemplateColumn CreateGripColumn()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, "\uE76F");
        text.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        text.SetValue(TextBlock.FontSizeProperty, 10.0);
        text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.OpacityProperty, 0.55);
        text.SetResourceReference(TextBlock.ForegroundProperty, "App.MutedBrush");

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(FrameworkElement.CursorProperty, Cursors.SizeNS);
        border.SetValue(
            FrameworkElement.ToolTipProperty,
            "Drag to reorder. Alt+Up / Alt+Down to move.");
        border.SetBinding(
            UIElement.VisibilityProperty,
            new Binding(nameof(KeyValueItemViewModel.HasContent))
            {
                Converter = BoolToVis
            });
        border.AppendChild(text);

        var template = new DataTemplate { VisualTree = border };
        var column = new DataGridTemplateColumn
        {
            Header = "",
            Width = new DataGridLength(24),
            MinWidth = 24,
            MaxWidth = 24,
            CanUserResize = false,
            CanUserSort = false,
            IsReadOnly = true,
            CellTemplate = template
        };
        column.SetValue(IsGripColumnProperty, true);
        return column;
    }

    private static void OnCurrentCellChanged(object? sender, EventArgs e)
    {
        if (sender is not DataGrid grid
            || grid.CurrentColumn is not { } column
            || !IsGripColumn(column)
            || grid.CurrentItem is null)
        {
            return;
        }

        var next = grid.Columns.FirstOrDefault(c =>
            !IsGripColumn(c) && c.Visibility == Visibility.Visible);
        if (next is null)
        {
            return;
        }

        grid.Dispatcher.BeginInvoke(
            () =>
            {
                if (grid.CurrentColumn is { } current && IsGripColumn(current) && grid.CurrentItem is not null)
                {
                    grid.CurrentCell = new DataGridCellInfo(grid.CurrentItem, next);
                }
            },
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.Up or Key.Down) || Keyboard.Modifiers != ModifierKeys.Alt)
        {
            return;
        }

        e.Handled = true;
        TryNudge(grid, key == Key.Up ? -1 : 1);
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid
            || e.ClickCount != 1
            || !IsGripSource(e.OriginalSource as DependencyObject, out var item)
            || GetList(grid) is not { } list)
        {
            return;
        }

        var lastMovable = KeyValueListReorder.LastMovableIndex(list);
        if (list.IndexOf(item) < 0 || list.IndexOf(item) > lastMovable)
        {
            return;
        }

        if (!KeyValueGridSelection.IsInSelection(grid, item))
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                KeyValueGridSelection.SelectForContextMenu(grid, item, addWithControl: true);
            }
            else
            {
                KeyValueGridSelection.SelectForContextMenu(grid, item, addWithControl: false);
            }
        }
        else
        {
            grid.CurrentItem = item;
        }

        var state = GetState(grid);
        state.Pending = true;
        state.Start = e.GetPosition(grid);
        state.Rows = GetMovingRows(grid, list, item);
        e.Handled = true;
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not DataGrid grid
            || e.LeftButton != MouseButtonState.Pressed
            || GetState(grid) is not { Pending: true, Rows: { Count: > 0 } rows } state)
        {
            return;
        }

        var pos = e.GetPosition(grid);
        if (Math.Abs(pos.X - state.Start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - state.Start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        state.Pending = false;
        CommitEdits(grid);
        var data = new DataObject(DragFormat, new RowDragPayload(grid, rows));
        try
        {
            DragDrop.DoDragDrop(grid, data, DragDropEffects.Move);
        }
        finally
        {
            ClearAdorner(grid);
            state.Rows = null;
        }
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            var state = GetState(grid);
            state.Pending = false;
            state.Rows = null;
        }
    }

    private static void OnQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed)
        {
            e.Action = DragAction.Cancel;
            if (sender is DataGrid grid)
            {
                ClearAdorner(grid);
            }
        }
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is not DataGrid grid || !TryGetPayload(e, grid, out _))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        AutoScroll(grid, e.GetPosition(grid));

        if (TryGetInsertIndex(grid, e, out _, out var lineY))
        {
            ShowAdorner(grid, lineY);
        }
        else
        {
            ClearAdorner(grid);
        }
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var pos = e.GetPosition(grid);
        if (pos.X < 0 || pos.Y < 0 || pos.X > grid.ActualWidth || pos.Y > grid.ActualHeight)
        {
            ClearAdorner(grid);
        }
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!TryGetPayload(e, grid, out var payload))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            ClearAdorner(grid);
            return;
        }

        e.Handled = true;
        ClearAdorner(grid);
        CommitEdits(grid);

        if (!TryGetInsertIndex(grid, e, out var insertBefore, out _)
            || GetList(grid) is not { } list)
        {
            return;
        }

        var order = KeyValueListReorder.TryCreateRelocatedOrder(list, payload.Rows, insertBefore);
        ApplyOrder(grid, list, order, payload.Rows);
    }

    private static void TryNudge(DataGrid grid, int delta)
    {
        CommitEdits(grid);
        if (GetList(grid) is not { } list)
        {
            return;
        }

        var moving = GetMovingRows(grid, list, fallback: grid.CurrentItem as KeyValueItemViewModel);
        var order = KeyValueListReorder.TryCreateNudgedOrder(list, moving, delta);
        ApplyOrder(grid, list, order, moving);
    }

    private static void ApplyOrder(
        DataGrid grid,
        ObservableCollection<KeyValueItemViewModel> list,
        List<KeyValueItemViewModel>? order,
        IReadOnlyList<KeyValueItemViewModel> moving)
    {
        if (order is null || grid.DataContext is not IKeyValueListSortHost host)
        {
            return;
        }

        if (!host.TryApplyKeyValueOrder(list, order))
        {
            return;
        }

        foreach (var column in grid.Columns)
        {
            column.SortDirection = null;
        }

        RestoreSelection(grid, moving);
    }

    private static List<KeyValueItemViewModel> GetMovingRows(
        DataGrid grid,
        IList<KeyValueItemViewModel> list,
        KeyValueItemViewModel? fallback)
    {
        var lastMovable = KeyValueListReorder.LastMovableIndex(list);
        var selected = KeyValueGridSelection.SnapshotSelectedRows(grid)
            .Where(item =>
            {
                var index = list.IndexOf(item);
                return index >= 0 && index <= lastMovable;
            })
            .ToList();

        if (selected.Count > 0)
        {
            return selected;
        }

        if (fallback is not null)
        {
            var index = list.IndexOf(fallback);
            if (index >= 0 && index <= lastMovable)
            {
                return [fallback];
            }
        }

        return [];
    }

    private static void RestoreSelection(DataGrid grid, IReadOnlyList<KeyValueItemViewModel> moving)
    {
        if (moving.Count == 0)
        {
            return;
        }

        try
        {
            grid.UnselectAll();
            foreach (var item in moving)
            {
                if (!grid.SelectedItems.Contains(item))
                {
                    grid.SelectedItems.Add(item);
                }
            }

            grid.CurrentItem = moving[0];
            grid.ScrollIntoView(moving[0]);
        }
        catch
        {
            grid.SelectedItem = moving[0];
        }
    }

    private static bool TryGetInsertIndex(
        DataGrid grid,
        DragEventArgs e,
        out int insertBefore,
        out double lineY)
    {
        insertBefore = 0;
        lineY = 0;
        if (GetList(grid) is not { } list)
        {
            return false;
        }

        var lastMovable = KeyValueListReorder.LastMovableIndex(list);
        var maxInsert = lastMovable + 1;
        if (maxInsert < 0)
        {
            return false;
        }

        var pos = e.GetPosition(grid);
        DataGridRow? hitRow = null;
        if (grid.InputHitTest(pos) is DependencyObject hit)
        {
            hitRow = FindAncestor<DataGridRow>(hit);
        }

        if (hitRow?.Item is KeyValueItemViewModel item)
        {
            var index = list.IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            var rel = e.GetPosition(hitRow);
            var after = rel.Y >= hitRow.ActualHeight / 2;
            insertBefore = after ? index + 1 : index;
            insertBefore = Math.Clamp(insertBefore, 0, Math.Max(0, maxInsert));

            var rowOrigin = hitRow.TransformToAncestor(grid).Transform(new Point(0, 0));
            lineY = after ? rowOrigin.Y + hitRow.ActualHeight : rowOrigin.Y;
            return true;
        }

        insertBefore = pos.Y < 8 ? 0 : maxInsert;
        insertBefore = Math.Clamp(insertBefore, 0, Math.Max(0, maxInsert));
        lineY = EstimateLineY(grid, list, insertBefore);
        return true;
    }

    private static double EstimateLineY(
        DataGrid grid,
        IList<KeyValueItemViewModel> list,
        int insertBefore)
    {
        if (insertBefore < list.Count
            && grid.ItemContainerGenerator.ContainerFromIndex(insertBefore) is DataGridRow row)
        {
            return row.TransformToAncestor(grid).Transform(new Point(0, 0)).Y;
        }

        if (insertBefore > 0
            && grid.ItemContainerGenerator.ContainerFromIndex(insertBefore - 1) is DataGridRow previous)
        {
            var origin = previous.TransformToAncestor(grid).Transform(new Point(0, 0));
            return origin.Y + previous.ActualHeight;
        }

        return Math.Max(8, grid.ActualHeight - 8);
    }

    private static void ShowAdorner(DataGrid grid, double y)
    {
        var state = GetState(grid);
        if (state.Adorner is null)
        {
            var layer = AdornerLayer.GetAdornerLayer(grid);
            if (layer is null)
            {
                return;
            }

            var brush = grid.TryFindResource("App.AccentBrush") as Brush ?? Brushes.DodgerBlue;
            state.Adorner = new RowDropAdorner(grid, brush);
            layer.Add(state.Adorner);
        }

        state.Adorner.SetY(y);
    }

    private static void ClearAdorner(DataGrid grid)
    {
        var state = GetState(grid);
        if (state.Adorner is null)
        {
            return;
        }

        AdornerLayer.GetAdornerLayer(grid)?.Remove(state.Adorner);
        state.Adorner = null;
    }

    private static void AutoScroll(DataGrid grid, Point pos)
    {
        if (FindDescendant<ScrollViewer>(grid) is not { } viewer)
        {
            return;
        }

        const double margin = 28;
        if (pos.Y < margin)
        {
            viewer.LineUp();
        }
        else if (pos.Y > grid.ActualHeight - margin)
        {
            viewer.LineDown();
        }
    }

    private static bool TryGetPayload(DragEventArgs e, DataGrid grid, out RowDragPayload payload)
    {
        payload = null!;
        if (!e.Data.GetDataPresent(DragFormat)
            || e.Data.GetData(DragFormat) is not RowDragPayload typed
            || !ReferenceEquals(typed.Grid, grid)
            || typed.Rows.Count == 0)
        {
            return false;
        }

        payload = typed;
        return true;
    }

    private static bool IsGripSource(DependencyObject? source, out KeyValueItemViewModel item)
    {
        item = null!;
        var cell = FindAncestor<DataGridCell>(source);
        if (cell?.Column is not { } column
            || !IsGripColumn(column)
            || FindAncestor<DataGridRow>(cell) is not { Item: KeyValueItemViewModel rowItem })
        {
            return false;
        }

        item = rowItem;
        return true;
    }

    private static void CommitEdits(DataGrid grid)
    {
        try
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        }
        catch
        {
            // Editing may already be closed.
        }
    }

    private static ObservableCollection<KeyValueItemViewModel>? GetList(DataGrid grid)
    {
        var source = grid.ItemsSource is ICollectionView view
            ? view.SourceCollection
            : grid.ItemsSource;
        return source as ObservableCollection<KeyValueItemViewModel>;
    }

    private static DragState GetState(DataGrid grid) => States.GetOrCreateValue(grid);

    private static bool IsGripColumn(DataGridColumn column) =>
        column.GetValue(IsGripColumnProperty) is true;

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = GetParentSafe(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static DependencyObject? GetParentSafe(DependencyObject? current)
    {
        if (current is null)
        {
            return null;
        }

        try
        {
            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                return VisualTreeHelper.GetParent(current);
            }
        }
        catch (InvalidOperationException)
        {
            // Fall through to logical parent.
        }

        return current switch
        {
            FrameworkContentElement content => content.Parent,
            FrameworkElement fe => fe.Parent,
            _ => LogicalTreeHelper.GetParent(current)
        };
    }

    private sealed class DragState
    {
        public Point Start;
        public bool Pending;
        public List<KeyValueItemViewModel>? Rows;
        public RowDropAdorner? Adorner;
    }

    private sealed class RowDragPayload
    {
        public RowDragPayload(DataGrid grid, IReadOnlyList<KeyValueItemViewModel> rows)
        {
            Grid = grid;
            Rows = rows;
        }

        public DataGrid Grid { get; }

        public IReadOnlyList<KeyValueItemViewModel> Rows { get; }
    }

    private sealed class RowDropAdorner : Adorner
    {
        private readonly Pen _pen;
        private double _y;

        public RowDropAdorner(UIElement adorned, Brush brush)
            : base(adorned)
        {
            IsHitTestVisible = false;
            _pen = new Pen(brush, 2)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            _pen.Freeze();
        }

        public void SetY(double y)
        {
            _y = y;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var width = ((FrameworkElement)AdornedElement).ActualWidth;
            drawingContext.DrawLine(_pen, new Point(8, _y), new Point(Math.Max(16, width - 8), _y));
        }
    }
}
