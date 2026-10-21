using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace RequestExplorer.Views.GridHelpers;

/// <summary>
/// When an element in a grid row is hidden, collapses that row's height so a
/// resizable inherited/local split does not leave empty space.
/// </summary>
public static class CollapsibleGridRow
{
    public static readonly DependencyProperty CollapseWhenHiddenProperty =
        DependencyProperty.RegisterAttached(
            "CollapseWhenHidden",
            typeof(bool),
            typeof(CollapsibleGridRow),
            new PropertyMetadata(false, OnCollapseWhenHiddenChanged));

    public static void SetCollapseWhenHidden(DependencyObject element, bool value) =>
        element.SetValue(CollapseWhenHiddenProperty, value);

    public static bool GetCollapseWhenHidden(DependencyObject element) =>
        (bool)element.GetValue(CollapseWhenHiddenProperty);

    private static void OnCollapseWhenHiddenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnIsVisibleChanged;
        element.Loaded -= OnLoaded;
        if (e.NewValue is true)
        {
            element.IsVisibleChanged += OnIsVisibleChanged;
            element.Loaded += OnLoaded;
            SyncRow(element);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            SyncRow(element);
        }
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            SyncRow(element);
        }
    }

    private static void SyncRow(FrameworkElement element)
    {
        var grid = FindAncestor<Grid>(element);
        if (grid is null)
        {
            return;
        }

        var row = Grid.GetRow(element);
        if (row < 0 || row >= grid.RowDefinitions.Count)
        {
            return;
        }

        var definition = grid.RowDefinitions[row];
        if (element.IsVisible)
        {
            if (element is GridSplitter)
            {
                definition.Height = new GridLength(8);
                definition.MinHeight = 8;
                definition.MaxHeight = 8;
            }
            else
            {
                definition.MinHeight = 56;
                definition.MaxHeight = 480;
                if (definition.Height.GridUnitType != GridUnitType.Pixel
                    || definition.Height.Value < 56)
                {
                    definition.Height = new GridLength(140);
                }
            }
        }
        else
        {
            definition.MinHeight = 0;
            definition.MaxHeight = 0;
            definition.Height = new GridLength(0);
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
