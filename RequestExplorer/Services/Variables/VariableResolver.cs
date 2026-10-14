using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RequestExplorer.Models;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

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

    /// <summary>
    /// Scope starting at <paramref name="node"/> and walking ancestors (nearest wins).
    /// Use for container editors; for requests prefer <see cref="BuildScope"/> (parents only).
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildScopeFrom(TreeNodeViewModel? node)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

    public static bool TryFindTokenAt(string? text, int charIndex, out string name, out int start, out int length)
    {
        name = string.Empty;
        start = 0;
        length = 0;
        if (string.IsNullOrEmpty(text) || charIndex < 0 || charIndex > text.Length)
        {
            return false;
        }

        foreach (Match match in TokenRegex().Matches(text))
        {
            var end = match.Index + match.Length;
            // Allow hovering the character just after the token (caret-style).
            if (charIndex >= match.Index && charIndex <= end)
            {
                name = match.Groups[1].Value.Trim();
                if (name.Length == 0)
                {
                    return false;
                }

                start = match.Index;
                length = match.Length;
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> FindTokenNames(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TokenRegex().Matches(text))
        {
            var name = match.Groups[1].Value.Trim();
            if (name.Length > 0 && seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    public static string FormatTokenTooltip(
        string name,
        IReadOnlyDictionary<string, string> scope,
        int valueMaxChars = 64)
    {
        if (scope.TryGetValue(name, out var value))
        {
            return $"{name} = {Truncate(value, valueMaxChars)}";
        }

        return $"{name} (not defined)";
    }

    public static string FormatTokensTooltip(
        IEnumerable<string> names,
        IReadOnlyDictionary<string, string> scope,
        int maxLines = 20,
        int valueMaxChars = 64)
    {
        var sb = new StringBuilder();
        var lines = 0;
        foreach (var name in names)
        {
            if (lines >= maxLines)
            {
                sb.Append('…');
                break;
            }

            if (lines > 0)
            {
                sb.AppendLine();
            }

            sb.Append(FormatTokenTooltip(name, scope, valueMaxChars));
            lines++;
        }

        return sb.ToString();
    }

    public static string Truncate(string? value, int maxChars)
    {
        value ??= string.Empty;
        if (maxChars < 1 || value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars] + "…";
    }

    public static IReadOnlyList<string> CollectReferencedNames(RequestDefinition request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectFromText(request.Address, names);
        CollectFromText(request.Body.Content, names);
        CollectFromText(request.Body.ContentType, names);

        foreach (var item in request.Params)
        {
            CollectFromText(item.Key, names);
            CollectFromText(item.Value, names);
        }

        foreach (var item in request.Headers)
        {
            CollectFromText(item.Key, names);
            CollectFromText(item.Value, names);
        }

        var auth = request.Authentication;
        CollectFromText(auth.BearerToken, names);
        CollectFromText(auth.BasicUsername, names);
        CollectFromText(auth.BasicPassword, names);
        CollectFromText(auth.ApiKeyName, names);
        CollectFromText(auth.ApiKeyValue, names);

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CollectFromText(string? text, ISet<string> names)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (Match match in TokenRegex().Matches(text))
        {
            var name = match.Groups[1].Value.Trim();
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }
    }

    /// <summary>
    /// URL-encodes a query key/value while leaving <c>{{token}}</c> segments literal
    /// so placeholders stay readable in the address bar and exports.
    /// </summary>
    public static string EscapeDataStringPreservingTokens(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var matches = TokenRegex().Matches(value);
        if (matches.Count == 0)
        {
            return Uri.EscapeDataString(value);
        }

        var sb = new StringBuilder(value.Length * 2);
        var last = 0;
        foreach (Match match in matches)
        {
            if (match.Index > last)
            {
                sb.Append(Uri.EscapeDataString(value[last..match.Index]));
            }

            sb.Append(match.Value);
            last = match.Index + match.Length;
        }

        if (last < value.Length)
        {
            sb.Append(Uri.EscapeDataString(value[last..]));
        }

        return sb.ToString();
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
