using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RequestExplorer.Services.Variables;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Views;

/// <summary>
/// Hover tooltips for <c>{{variable}}</c> tokens (resolved value) and for
/// param/header/variable row keys (shows that item's value).
/// </summary>
public static class VariableTokenToolTips
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(VariableTokenToolTips),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.MouseMove -= OnMouseMove;
        element.MouseLeave -= OnMouseLeave;

        if (e.NewValue is true)
        {
            element.MouseMove += OnMouseMove;
            element.MouseLeave += OnMouseLeave;
        }
        else
        {
            ClearTooltip(element);
        }

        // Same fields also offer "Replace with {{variable}}" when selection matches a value.
        if (element is TextBox)
        {
            ReplaceMatchingVariableMenu.SetIsEnabled(element, e.NewValue is true);
        }
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ClearTooltip(element);
        }
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var text = GetText(element);
        var point = e.GetPosition(element);
        var charIndex = GetCharacterIndex(element, point);
        var scope = ResolveScope(element);

        string? tip = null;
        if (charIndex is int index
            && VariableResolver.TryFindTokenAt(text, index, out var tokenName, out _, out _))
        {
            tip = VariableResolver.FormatTokenTooltip(tokenName, scope);
        }
        else if (VariableResolver.FindTokenNames(text) is { Count: > 0 } names)
        {
            // In the field but not on a specific token — list all refs in this text.
            tip = VariableResolver.FormatTokensTooltip(names, scope);
        }
        else if (TryFormatRowItemTooltip(element, out var rowTip))
        {
            // Param / header / variable key row: show that item's value.
            tip = rowTip;
        }

        if (string.IsNullOrEmpty(tip))
        {
            ClearTooltip(element);
            return;
        }

        if (!Equals(element.ToolTip as string, tip))
        {
            element.ToolTip = tip;
        }
    }

    private static void ClearTooltip(FrameworkElement element)
    {
        if (element.ToolTip is string)
        {
            element.ToolTip = null;
        }
    }

    private static bool TryFormatRowItemTooltip(FrameworkElement element, out string? tip)
    {
        tip = null;
        switch (element.DataContext)
        {
            case KeyValueItemViewModel kv when kv.HasKey:
                tip = $"{kv.Key.Trim()} = {VariableResolver.Truncate(kv.Value, 64)}";
                return true;
            case InheritedVariableRow inherited:
                tip = $"{inherited.Key} = {VariableResolver.Truncate(inherited.Value, 64)}"
                      + (string.IsNullOrEmpty(inherited.SourceName)
                          ? string.Empty
                          : $"  ({inherited.SourceName})");
                return true;
            default:
                return false;
        }
    }

    private static string GetText(FrameworkElement element) =>
        element switch
        {
            TextBox tb => tb.Text ?? string.Empty,
            TextBlock block => block.Text ?? string.Empty,
            _ => string.Empty
        };

    private static int? GetCharacterIndex(FrameworkElement element, Point point)
    {
        try
        {
            if (element is TextBox textBox)
            {
                var index = textBox.GetCharacterIndexFromPoint(point, snapToText: true);
                return index >= 0 ? index : null;
            }

            if (element is TextBlock textBlock)
            {
                var pointer = textBlock.GetPositionFromPoint(point, snapToText: true);
                if (pointer is null)
                {
                    return null;
                }

                var range = new System.Windows.Documents.TextRange(textBlock.ContentStart, pointer);
                return range.Text.Length;
            }
        }
        catch
        {
            // Hit-testing can throw while the visual is updating.
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> ResolveScope(FrameworkElement element)
    {
        var request = FindAncestorDataContext<RequestNodeViewModel>(element);
        if (request is not null)
        {
            return VariableResolver.BuildScope(request);
        }

        var container = FindAncestorDataContext<ContainerNodeViewModel>(element);
        if (container is not null)
        {
            return VariableResolver.BuildScopeFrom(container);
        }

        // DataGrid cell: DataContext is the row; walk visual tree for editor DataContext.
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: RequestNodeViewModel req })
            {
                return VariableResolver.BuildScope(req);
            }

            if (current is FrameworkElement { DataContext: ContainerNodeViewModel c })
            {
                return VariableResolver.BuildScopeFrom(c);
            }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static T? FindAncestorDataContext<T>(DependencyObject? start)
        where T : class
    {
        for (DependencyObject? current = start; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: T match })
            {
                return match;
            }
        }

        return null;
    }
}
