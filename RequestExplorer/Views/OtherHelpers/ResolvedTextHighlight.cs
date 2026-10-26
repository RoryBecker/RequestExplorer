using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using RequestExplorer.Services.Variables;

namespace RequestExplorer.Views.OtherHelpers;

/// <summary>Renders <see cref="ResolvedTextSpan"/> slices on a <see cref="TextBlock"/>, bolding variable substitutions.</summary>
public static class ResolvedTextHighlight
{
    public static readonly DependencyProperty SpansProperty =
        DependencyProperty.RegisterAttached(
            "Spans",
            typeof(IEnumerable<ResolvedTextSpan>),
            typeof(ResolvedTextHighlight),
            new PropertyMetadata(null, OnSpansChanged));

    public static IEnumerable<ResolvedTextSpan>? GetSpans(TextBlock obj) =>
        (IEnumerable<ResolvedTextSpan>?)obj.GetValue(SpansProperty);

    public static void SetSpans(TextBlock obj, IEnumerable<ResolvedTextSpan>? value) =>
        obj.SetValue(SpansProperty, value);

    private static void OnSpansChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
        {
            return;
        }

        block.Inlines.Clear();
        if (e.NewValue is not IEnumerable<ResolvedTextSpan> spans)
        {
            return;
        }

        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            var run = new Run(span.Text)
            {
                FontWeight = span.IsFromVariable ? FontWeights.Bold : FontWeights.Normal
            };
            if (span.IsFromVariable)
            {
                run.SetResourceReference(TextElement.ForegroundProperty, "App.BrightTextBrush");
            }

            block.Inlines.Add(run);
        }
    }
}
