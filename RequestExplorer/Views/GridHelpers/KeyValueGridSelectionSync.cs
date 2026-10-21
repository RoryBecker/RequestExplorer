using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RequestExplorer.Views.GridHelpers;

/// <summary>Scrolls the DataGrid's selected item into view when selection changes.</summary>
public static class KeyValueGridSelectionSync
{
    public static readonly DependencyProperty BringSelectedIntoViewProperty =
        DependencyProperty.RegisterAttached(
            "BringSelectedIntoView",
            typeof(bool),
            typeof(KeyValueGridSelectionSync),
            new PropertyMetadata(false, OnBringSelectedIntoViewChanged));

    public static void SetBringSelectedIntoView(DependencyObject element, bool value) =>
        element.SetValue(BringSelectedIntoViewProperty, value);

    public static bool GetBringSelectedIntoView(DependencyObject element) =>
        (bool)element.GetValue(BringSelectedIntoViewProperty);

    private static void OnBringSelectedIntoViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        grid.SelectionChanged -= OnSelectionChanged;
        if (e.NewValue is true)
        {
            grid.SelectionChanged += OnSelectionChanged;
        }
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid || grid.SelectedItem is null)
        {
            return;
        }

        var item = grid.SelectedItem;
        grid.Dispatcher.BeginInvoke(
            () =>
            {
                if (!ReferenceEquals(grid.SelectedItem, item))
                {
                    return;
                }

                grid.ScrollIntoView(item);
                grid.UpdateLayout();
                if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                {
                    row.BringIntoView();
                }
            },
            DispatcherPriority.Loaded);
    }
}
