using RequestExplorer.Models;
using RequestExplorer.Services.Preferences;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Headers;

/// <summary>
/// Resolves effective request headers from the node and its ancestors (nearest enabled wins).
/// </summary>
public static class HeaderInheritance
{
    /// <summary>
    /// Builds the header list used for Send / export: local request headers first (list order),
    /// then nearest ancestor headers for names not already taken by an enabled local row.
    /// Non-inheritable ancestor rows seal the name (no farther fall-through).
    /// </summary>
    public static List<KeyValueItem> BuildEffectiveHeaders(RequestNodeViewModel request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<KeyValueItem>();

        foreach (var header in request.Headers)
        {
            if (!header.IsEnabled || !header.HasKey)
            {
                continue;
            }

            var key = header.Key.Trim();
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(CloneItem(header.Model, key));
        }

        for (var node = request.Parent; node is not null; node = node.Parent)
        {
            if (node is not ContainerNodeViewModel container)
            {
                continue;
            }

            foreach (var header in container.Headers)
            {
                if (!header.IsEnabled || !header.HasKey)
                {
                    continue;
                }

                var key = header.Key.Trim();
                if (!seen.Add(key))
                {
                    continue;
                }

                if (!InheritedGridPreferences.IsEffectivelyInheritable(header.IsInheritable))
                {
                    continue;
                }

                result.Add(CloneItem(header.Model, key));
            }
        }

        return result;
    }

    /// <summary>Replaces <paramref name="request"/>.Headers with effective inherited headers (in place).</summary>
    public static void ApplyEffectiveHeaders(RequestDefinition request, RequestNodeViewModel requestNode)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(requestNode);

        request.Headers = BuildEffectiveHeaders(requestNode);
    }

    private static KeyValueItem CloneItem(KeyValueItem source, string key) =>
        new()
        {
            IsEnabled = source.IsEnabled,
            Key = key,
            Value = source.Value ?? string.Empty,
            IsSecret = source.IsSecret,
            IsInheritable = source.IsInheritable
        };
}
