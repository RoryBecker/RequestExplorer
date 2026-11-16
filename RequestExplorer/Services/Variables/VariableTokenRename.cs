using RequestExplorer.Services.Preferences;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

/// <summary>
/// Renames a variable definition and rewrites <c>{{name}}</c> usages on descendant requests
/// that resolve to that definition.
/// </summary>
public static class VariableTokenRename
{
    /// <summary>
    /// Descendant requests under <paramref name="owner"/> that reference <paramref name="oldName"/>
    /// and for which <paramref name="definition"/> is the winning ancestor definition.
    /// </summary>
    public static IReadOnlyList<RequestNodeViewModel> FindRequestsUsingDefinition(
        ContainerNodeViewModel owner,
        KeyValueItemViewModel definition,
        string oldName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(oldName) || !definition.HasKey)
        {
            return [];
        }

        var results = new List<RequestNodeViewModel>();
        foreach (var request in EnumerateRequests(owner))
        {
            if (!RequestReferencesName(request, oldName))
            {
                continue;
            }

            if (!IsWinningDefinition(request, definition, oldName))
            {
                continue;
            }

            results.Add(request);
        }

        return results;
    }

    public static bool RequestReferencesName(RequestNodeViewModel request, string name)
    {
        if (VariableResolver.ContainsTokenName(request.Address, name)
            || VariableResolver.ContainsTokenName(request.BodyContent, name)
            || VariableResolver.ContainsTokenName(request.BodyContentType, name)
            || VariableResolver.ContainsTokenName(request.BearerToken, name)
            || VariableResolver.ContainsTokenName(request.BasicUsername, name)
            || VariableResolver.ContainsTokenName(request.BasicPassword, name)
            || VariableResolver.ContainsTokenName(request.ApiKeyName, name)
            || VariableResolver.ContainsTokenName(request.ApiKeyValue, name))
        {
            return true;
        }

        foreach (var item in request.Headers)
        {
            if (VariableResolver.ContainsTokenName(item.Key, name)
                || VariableResolver.ContainsTokenName(item.Value, name))
            {
                return true;
            }
        }

        // Params usually mirror the address query; still check in case of drift.
        foreach (var item in request.Params)
        {
            if (VariableResolver.ContainsTokenName(item.Key, name)
                || VariableResolver.ContainsTokenName(item.Value, name))
            {
                return true;
            }
        }

        foreach (var field in request.FormFields)
        {
            if (VariableResolver.ContainsTokenName(field.Key, name))
            {
                return true;
            }

            if (field.IsTextType && VariableResolver.ContainsTokenName(field.Value, name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when walking from <paramref name="request"/>, <paramref name="definition"/> is the
    /// nearest enabled inheritable match for <paramref name="name"/>.
    /// </summary>
    public static bool IsWinningDefinition(
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

                if (!InheritedGridPreferences.IsEffectivelyInheritable(item.IsInheritable))
                {
                    // Chain break — this name does not resolve from farther ancestors either.
                    return false;
                }

                return ReferenceEquals(item, definition);
            }
        }

        return false;
    }

    /// <summary>
    /// All variable rows under <paramref name="root"/> whose key matches <paramref name="name"/>.
    /// </summary>
    public static IReadOnlyList<(ContainerNodeViewModel Container, KeyValueItemViewModel Item)>
        FindDefinitionsByName(TreeNodeViewModel root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var results = new List<(ContainerNodeViewModel, KeyValueItemViewModel)>();
        foreach (var container in EnumerateContainers(root))
        {
            foreach (var item in container.Variables)
            {
                if (item.HasKey
                    && string.Equals(item.Key.Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add((container, item));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// All requests under <paramref name="root"/> that reference <paramref name="name"/> in any field.
    /// </summary>
    public static IReadOnlyList<RequestNodeViewModel> FindRequestsReferencingName(
        TreeNodeViewModel root,
        string name)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        return EnumerateRequests(root)
            .Where(r => RequestReferencesName(r, name))
            .ToList();
    }

    public static IEnumerable<ContainerNodeViewModel> EnumerateContainers(TreeNodeViewModel root)
    {
        if (root is ContainerNodeViewModel container)
        {
            yield return container;
        }

        foreach (var child in root.Children)
        {
            foreach (var nested in EnumerateContainers(child))
            {
                yield return nested;
            }
        }
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
