using System.Text.Json;
using System.Text.RegularExpressions;
using RequestExplorer.Models;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Services;

/// <summary>
/// Resolves <c>{{variable}}</c> tokens using enabled variables from the request's
/// parent chain (nearest ancestor wins).
/// </summary>
public static partial class VariableResolver
{
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    public static IReadOnlyDictionary<string, string> BuildScope(TreeNodeViewModel? requestNode)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var node = requestNode?.Parent;
        while (node is not null)
        {
            if (node is ContainerNodeViewModel container)
            {
                foreach (var item in container.Variables)
                {
                    if (!item.IsEnabled || string.IsNullOrWhiteSpace(item.Key))
                    {
                        continue;
                    }

                    var key = item.Key.Trim();
                    if (!map.ContainsKey(key))
                    {
                        map[key] = item.Value ?? string.Empty;
                    }
                }
            }

            node = node.Parent;
        }

        return map;
    }

    public static string Substitute(string? text, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(text) || variables.Count == 0)
        {
            return text ?? string.Empty;
        }

        return TokenRegex().Replace(text, match =>
        {
            var name = match.Groups[1].Value.Trim();
            return variables.TryGetValue(name, out var value) ? value : match.Value;
        });
    }

    /// <summary>Returns a deep copy of <paramref name="source"/> with variables substituted.</summary>
    public static RequestDefinition Resolve(RequestDefinition source, TreeNodeViewModel requestNode)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requestNode);

        var variables = BuildScope(requestNode);
        var clone = Clone(source);
        if (variables.Count == 0)
        {
            return clone;
        }

        ApplyInPlace(clone, variables);
        return clone;
    }

    private static RequestDefinition Clone(RequestDefinition source)
    {
        var json = JsonSerializer.Serialize(source, CloneOptions);
        return JsonSerializer.Deserialize<RequestDefinition>(json, CloneOptions)
               ?? new RequestDefinition();
    }

    private static void ApplyInPlace(RequestDefinition request, IReadOnlyDictionary<string, string> variables)
    {
        request.Address = Substitute(request.Address, variables);
        request.Body.Content = Substitute(request.Body.Content, variables);
        if (request.Body.ContentType is not null)
        {
            request.Body.ContentType = Substitute(request.Body.ContentType, variables);
        }

        foreach (var item in request.Params)
        {
            item.Key = Substitute(item.Key, variables);
            item.Value = Substitute(item.Value, variables);
        }

        foreach (var item in request.Headers)
        {
            item.Key = Substitute(item.Key, variables);
            item.Value = Substitute(item.Value, variables);
        }

        var auth = request.Authentication;
        auth.BearerToken = Substitute(auth.BearerToken, variables);
        auth.BasicUsername = Substitute(auth.BasicUsername, variables);
        auth.BasicPassword = Substitute(auth.BasicPassword, variables);
        auth.ApiKeyName = Substitute(auth.ApiKeyName, variables);
        auth.ApiKeyValue = Substitute(auth.ApiKeyValue, variables);
    }
}
