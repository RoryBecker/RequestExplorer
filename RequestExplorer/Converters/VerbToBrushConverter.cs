using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RequestExplorer.Models;

namespace RequestExplorer.Converters;

public sealed class VerbToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GetBrush = Brush("#2563EB");
    private static readonly SolidColorBrush MutatingBrush = Brush("#059669");
    private static readonly SolidColorBrush DeleteBrush = Brush("#DC2626");
    private static readonly SolidColorBrush OtherBrush = Brush("#7C3AED");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HttpVerb verb)
        {
            return OtherBrush;
        }

        return verb switch
        {
            HttpVerb.GET or HttpVerb.HEAD or HttpVerb.OPTIONS => GetBrush,
            HttpVerb.POST or HttpVerb.PUT or HttpVerb.PATCH => MutatingBrush,
            HttpVerb.DELETE => DeleteBrush,
            _ => OtherBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Brush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
        brush.Freeze();
        return brush;
    }
}
