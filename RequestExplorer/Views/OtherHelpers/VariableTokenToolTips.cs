using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RequestExplorer.Services.Variables;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;
using RequestExplorer.Views.MenuHelpers;

namespace RequestExplorer.Views.OtherHelpers;

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

    /// <summary>Only one cell tip at a time — prevents a prior row's tip (e.g. a resolved @date) from lingering.</summary>
    private static FrameworkElement? _activeTipOwner;

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
        element.DataContextChanged -= OnDataContextChanged;

        if (e.NewValue is true)
        {
            // Avoid the post-close cooldown that skips tips when hopping between grid cells.
            ToolTipService.SetBetweenShowDelay(element, 0);
            element.MouseMove += OnMouseMove;
            element.MouseLeave += OnMouseLeave;
            element.DataContextChanged += OnDataContextChanged;
        }
        else
        {
            DetachTooltip(element);
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
            HideTooltip(element);
        }
    }

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // DataGrid recycles cell visuals; drop the previous row's tip immediately.
        if (sender is FrameworkElement element)
        {
            HideTooltip(element);
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
        else if (IsHoveringNameColumn(element, text)
                 && TryFormatRowItemTooltip(element, out var rowTip))
        {
            // Name/key column only: show that row's calculated value (not Source/Value/Status).
            tip = rowTip;
        }

        if (string.IsNullOrEmpty(tip))
        {
            HideTooltip(element);
            return;
        }

        ShowTooltip(element, tip);
    }

    /// <summary>
    /// True when this cell is the Key column (not Value / Status). Prefer the grid column
    /// over text equality so recycled cells or Value==Key cannot show the wrong tip.
    /// </summary>
    private static bool IsHoveringNameColumn(FrameworkElement element, string displayedText)
    {
        if (FindAncestor<DataGridCell>(element) is { Column: { } column })
        {
            return column.Header is string header
                   && header.Equals("Key", StringComparison.OrdinalIgnoreCase)
                   && element.DataContext is KeyValueItemViewModel or InheritedVariableRow;
        }

        return element.DataContext switch
        {
            KeyValueItemViewModel kv =>
                string.Equals(displayedText, kv.Key ?? string.Empty, StringComparison.Ordinal),
            InheritedVariableRow inherited =>
                string.Equals(displayedText, inherited.Key ?? string.Empty, StringComparison.Ordinal),
            _ => false
        };
    }

    private static T? FindAncestor<T>(DependencyObject? start)
        where T : DependencyObject
    {
        for (DependencyObject? current = start; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// Updates tip content in place. Clearing <see cref="FrameworkElement.ToolTip"/> while a popup
    /// is open (common when flicking across DataGrid rows) leaves ToolTipService unable to show
    /// again until focus/mouse resets — so we keep a <see cref="ToolTip"/> instance and only hide it.
    /// </summary>
    private static void ShowTooltip(FrameworkElement element, string text)
    {
        if (_activeTipOwner is not null && !ReferenceEquals(_activeTipOwner, element))
        {
            HideTooltip(_activeTipOwner);
        }

        _activeTipOwner = element;

        if (element.ToolTip is ToolTip existing)
        {
            if (!Equals(existing.Content as string, text))
            {
                existing.Content = text;
            }

            return;
        }

        element.ToolTip = new ToolTip { Content = text };
    }

    private static void HideTooltip(FrameworkElement element)
    {
        if (ReferenceEquals(_activeTipOwner, element))
        {
            _activeTipOwner = null;
        }

        if (element.ToolTip is ToolTip tip)
        {
            tip.IsOpen = false;
            tip.Content = null;
            return;
        }

        if (element.ToolTip is string)
        {
            element.ToolTip = null;
        }
    }

    private static void DetachTooltip(FrameworkElement element)
    {
        HideTooltip(element);
        element.ToolTip = null;
    }

    private static bool TryFormatRowItemTooltip(FrameworkElement element, out string? tip)
    {
        tip = null;
        switch (element.DataContext)
        {
            case KeyValueItemViewModel kv when kv.HasKey:
                tip = $"{kv.Key.Trim()} = {FormatRowValue(kv.Value)}";
                return true;
            case InheritedVariableRow inherited:
                // Key = value only (no source suffix — that "(…)" was mistaken for data).
                tip = $"{inherited.Key} = {FormatRowValue(inherited.VariableValue)}";
                return true;
            default:
                return false;
        }
    }

    private static string FormatRowValue(string? stored) =>
        VariableResolver.Truncate(VariableValueRecipes.Resolve(stored), 64);

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
