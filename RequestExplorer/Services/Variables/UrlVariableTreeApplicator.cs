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

        public bool IsShadowed { get; init; }

        public string ShadowReason { get; init; } = string.Empty;

        public string OriginalAddress => Request.Address ?? string.Empty;
    }

    /// <summary>
    /// Requests under <paramref name="scanRoot"/> whose URL contains this definition's
    /// resolved value. Rows where a closer definition wins are included as shadowed skips.
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

        var owner = FindOwner(scanRoot, definition);
        if (owner is null)
        {
            return [];
        }

        var value = ResolveDefinitionValue(owner, definition, name);
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var singleton = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [name] = value
        };

        var results = new List<RequestMatch>();
        foreach (var request in EnumerateRequests(scanRoot))
        {
            var matches = UrlVariableInjector.FindMatches(request.Address, singleton);
            if (matches.Count == 0)
            {
                continue;
            }

            var applied = UrlVariableInjector.ApplyWithSpans(request.Address, matches);
            if (string.Equals(applied.Text, request.Address ?? string.Empty, StringComparison.Ordinal))
            {
                continue;
            }

            var winning = FindWinningDefinition(request, name);
            var shadowed = winning.Item is not null && !ReferenceEquals(winning.Item, definition);
            results.Add(new RequestMatch
            {
                Request = request,
                Match = matches[0],
                AppliedAddress = shadowed ? request.Address ?? string.Empty : applied.Text,
                PreviewSpans = shadowed ? [] : applied.Spans,
                IsShadowed = shadowed,
                ShadowReason = shadowed && winning.Container is not null
                    ? $"Uses {name} on {winning.Container.Name}"
                    : string.Empty
            });
        }

        return results;
    }

    private static string ResolveDefinitionValue(
        ContainerNodeViewModel owner,
        KeyValueItemViewModel definition,
        string name)
    {
        var scope = VariableResolver.BuildScopeFrom(owner);
        if (IsFirstEnabledNamed(owner, definition, name)
            && scope.TryGetValue(name, out var value))
        {
            return value ?? string.Empty;
        }

        var raw = VariableValueRecipes.Resolve(definition.Value);
        return VariableResolver.Substitute(raw, scope);
    }

    private static bool IsFirstEnabledNamed(
        ContainerNodeViewModel owner,
        KeyValueItemViewModel definition,
        string name)
    {
        foreach (var item in owner.Variables)
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

        return false;
    }

    private static ContainerNodeViewModel? FindOwner(
        TreeNodeViewModel scanRoot,
        KeyValueItemViewModel definition)
    {
        for (var node = scanRoot; node is not null; node = node.Parent)
        {
            if (node is ContainerNodeViewModel container && container.Variables.Contains(definition))
            {
                return container;
            }
        }

        return null;
    }

    private static (ContainerNodeViewModel? Container, KeyValueItemViewModel? Item) FindWinningDefinition(
        RequestNodeViewModel request,
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

                return (container, item);
            }
        }

        return (null, null);
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
