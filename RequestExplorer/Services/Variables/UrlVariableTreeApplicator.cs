using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

/// <summary>
/// Finds descendant request URLs that still contain a specific variable's resolved value
/// so that value can be replaced with <c>{{name}}</c>.
/// </summary>
public static class UrlVariableTreeApplicator
{
    public sealed class RequestMatch
    {
        public required RequestNodeViewModel Request { get; init; }

        public required UrlVariableInjector.Match Match { get; init; }

        public required string AppliedAddress { get; init; }

        public required IReadOnlyList<ResolvedTextSpan> PreviewSpans { get; init; }

        public string OriginalAddress => Request.Address ?? string.Empty;
    }

    /// <summary>
    /// Requests under <paramref name="scanRoot"/> whose URL would change if
    /// <paramref name="definition"/> were injected, and for which that row is
    /// the nearest enabled definition of its name.
    /// </summary>
    public static IReadOnlyList<RequestMatch> FindAffectedRequests(
        TreeNodeViewModel scanRoot,
        KeyValueItemViewModel definition)
    {
        ArgumentNullException.ThrowIfNull(scanRoot);
        ArgumentNullException.ThrowIfNull(definition);

        var name = definition.Key?.Trim() ?? string.Empty;
        if (name.Length == 0 || !definition.IsEnabled)
        {
            return [];
        }

        var results = new List<RequestMatch>();
        foreach (var request in EnumerateRequests(scanRoot))
        {
            if (!IsWinningDefinition(request, definition, name))
            {
                continue;
            }

            var scope = VariableResolver.BuildScope(request);
            if (!scope.TryGetValue(name, out var value) || string.IsNullOrEmpty(value))
            {
                continue;
            }

            var matches = UrlVariableInjector.FindMatches(
                request.Address,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = value });
            if (matches.Count == 0)
            {
                continue;
            }

            var applied = UrlVariableInjector.ApplyWithSpans(request.Address, matches);
            if (string.Equals(applied.Text, request.Address ?? string.Empty, StringComparison.Ordinal))
            {
                continue;
            }

            results.Add(new RequestMatch
            {
                Request = request,
                Match = matches[0],
                AppliedAddress = applied.Text,
                PreviewSpans = applied.Spans
            });
        }

        return results;
    }

    private static bool IsWinningDefinition(
        RequestNodeViewModel request,
        KeyValueItemViewModel definition,
        string name)
    {
        for (var node = request.Parent; node is not null; node = node.Parent)
        {
            if (node is not ContainerNodeViewModel container)
            {
                continue;
            }

            foreach (var item in container.Variables)
            {
                if (!item.IsEnabled || !item.HasKey)
                {
                    continue;
                }

                if (!string.Equals(item.Key.Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return ReferenceEquals(item, definition);
            }
        }

        return false;
    }

    private static IEnumerable<RequestNodeViewModel> EnumerateRequests(TreeNodeViewModel root)
    {
        if (root is RequestNodeViewModel request)
        {
            yield return request;
            yield break;
        }

        foreach (var child in root.Children)
        {
            foreach (var nested in EnumerateRequests(child))
            {
                yield return nested;
            }
        }
    }
}
