using System.Text;

namespace RequestExplorer.Services.Variables;

/// <summary>
/// Finds literal URL slices that match enabled ancestor variable values so they can
/// be replaced with <c>{{name}}</c> tokens that resolve back to the same text.
/// </summary>
public static class UrlVariableInjector
{
    public sealed class Match
    {
        public required string Name { get; init; }

        public required string Value { get; init; }

        public required IReadOnlyList<(int Start, int Length)> Spans { get; init; }

        public int ReplacedLength { get; init; }
    }

    public static IReadOnlyList<Match> FindMatches(
        string? address,
        IReadOnlyDictionary<string, string> scope)
    {
        address ??= string.Empty;
        scope ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tokens = VariableResolver.GetTokenRanges(address);
        var results = new List<Match>();

        foreach (var pair in scope)
        {
            var name = pair.Key?.Trim() ?? string.Empty;
            var value = pair.Value ?? string.Empty;
            if (name.Length == 0 || value.Length == 0)
            {
                continue;
            }

            var spans = FindSpans(address, value, tokens);
            if (spans.Count == 0)
            {
                continue;
            }

            results.Add(new Match
            {
                Name = name,
                Value = value,
                Spans = spans,
                ReplacedLength = spans.Sum(s => s.Length)
            });
        }

        return results
            .OrderByDescending(m => m.ReplacedLength)
            .ThenByDescending(m => m.Value.Length)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Largest replacements first; skip a name when any of its spans overlap an already chosen span.
    /// </summary>
    public static IReadOnlySet<string> SuggestSelection(IEnumerable<Match> matches)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var occupied = new List<(int Start, int Length)>();
        foreach (var match in matches)
        {
            if (match.Spans.Count == 0 || SpansOverlapAny(match.Spans, occupied))
            {
                continue;
            }

            selected.Add(match.Name);
            occupied.AddRange(match.Spans);
        }

        return selected;
    }

    public static bool SelectionConflicts(IEnumerable<Match> selected)
    {
        var spans = selected.SelectMany(m => m.Spans).ToList();
        for (var i = 0; i < spans.Count; i++)
        {
            for (var j = i + 1; j < spans.Count; j++)
            {
                if (Overlaps(spans[i], spans[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static string Apply(string? address, IEnumerable<Match> selected) =>
        ApplyWithSpans(address, selected).Text;

    /// <summary>
    /// Builds the tokenized URL and marks injected <c>{{name}}</c> slices for preview highlighting.
    /// </summary>
    public static (string Text, IReadOnlyList<ResolvedTextSpan> Spans) ApplyWithSpans(
        string? address,
        IEnumerable<Match> selected)
    {
        var text = address ?? string.Empty;
        var replacements = selected
            .SelectMany(m => m.Spans.Select(s => (s.Start, s.Length, Token: "{{" + m.Name + "}}")))
            .Where(r => r.Start >= 0 && r.Length >= 0 && r.Start + r.Length <= text.Length)
            .OrderBy(r => r.Start)
            .ThenByDescending(r => r.Length)
            .ToList();

        var spans = new List<ResolvedTextSpan>();
        var sb = new StringBuilder();
        var last = 0;
        foreach (var replacement in replacements)
        {
            if (replacement.Start < last)
            {
                continue;
            }

            if (replacement.Start > last)
            {
                AppendSpan(sb, spans, text[last..replacement.Start], fromVariable: false);
            }

            AppendSpan(sb, spans, replacement.Token, fromVariable: true);
            last = replacement.Start + replacement.Length;
        }

        if (last < text.Length)
        {
            AppendSpan(sb, spans, text[last..], fromVariable: false);
        }

        return (sb.ToString(), spans);
    }

    private static void AppendSpan(
        StringBuilder sb,
        List<ResolvedTextSpan> spans,
        string piece,
        bool fromVariable)
    {
        if (piece.Length == 0)
        {
            return;
        }

        sb.Append(piece);
        if (spans.Count > 0 && spans[^1].IsFromVariable == fromVariable)
        {
            spans[^1] = spans[^1] with { Text = spans[^1].Text + piece };
            return;
        }

        spans.Add(new ResolvedTextSpan(piece, fromVariable));
    }

    private static IReadOnlyList<(int Start, int Length)> FindSpans(
        string address,
        string value,
        IReadOnlyList<(int Start, int Length)> tokens)
    {
        var spans = new List<(int Start, int Length)>();
        var start = 0;
        while (start <= address.Length - value.Length)
        {
            var index = address.IndexOf(value, start, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            if (!OverlapsAny((index, value.Length), tokens))
            {
                spans.Add((index, value.Length));
                start = index + value.Length;
            }
            else
            {
                start = index + 1;
            }
        }

        return spans;
    }

    private static bool SpansOverlapAny(
        IReadOnlyList<(int Start, int Length)> spans,
        IReadOnlyList<(int Start, int Length)> occupied) =>
        spans.Any(s => OverlapsAny(s, occupied));

    private static bool OverlapsAny(
        (int Start, int Length) span,
        IReadOnlyList<(int Start, int Length)> others) =>
        others.Any(other => Overlaps(span, other));

    private static bool Overlaps((int Start, int Length) a, (int Start, int Length) b) =>
        a.Start < b.Start + b.Length && b.Start < a.Start + a.Length;
}
