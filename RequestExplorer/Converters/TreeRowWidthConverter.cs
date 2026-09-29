using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace RequestExplorer.Converters;

/// <summary>
/// Makes a tree row fill from its indent to the TreeView's right edge so action buttons align.
/// values[0] = TreeView.ActualWidth, values[1] = row FrameworkElement (self).
/// </summary>
public sealed class TreeRowWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2
            || values[0] is not double treeWidth
            || double.IsNaN(treeWidth)
            || values[1] is not FrameworkElement row)
        {
            return double.NaN;
        }

        var tree = FindAncestor<TreeView>(row);
        if (tree is null)
        {
            return double.NaN;
        }

        try
        {
            var left = row.TransformToAncestor(tree).Transform(new Point(0, 0)).X;
            var width = treeWidth - left - 2;
            return width > 40 ? width : double.NaN;
        }
        catch (InvalidOperationException)
        {
            return double.NaN;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
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
