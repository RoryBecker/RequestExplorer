using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RequestExplorer.Models;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

/// <summary>
/// Resolves <c>{{variable}}</c> tokens using enabled variables from the request's
/// parent chain (nearest ancestor wins). Nested tokens in a variable's value
/// restart the lookup at the same origin (request or container).
/// </summary>
public static partial class VariableResolver
{
    public const int DefaultMaxNestingDepth = 10;
    public const int MinMaxNestingDepth = 1;
    public const int MaxMaxNestingDepth = 50;

    /// <summary>Active max nested lookups; MainViewModel syncs this from settings.</summary>
    public static int MaxNestingDepth { get; set; } = DefaultMaxNestingDepth;

    public static int ClampMaxNestingDepth(int value) =>
        Math.Clamp(value, MinMaxNestingDepth, MaxMaxNestingDepth);

    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    public static IReadOnlyDictionary<string, string> BuildScope(TreeNodeViewModel? requestNode) =>
        BuildResolvedScope(requestNode, includeOrigin: false);

    /// <summary>
    /// Scope starting at <paramref name="node"/> and walking ancestors (nearest wins).
    /// Use for container editors; for requests prefer <see cref="BuildScope"/> (parents only).
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildScopeFrom(TreeNodeViewModel? node) =>
        BuildResolvedScope(node, includeOrigin: true);

    public static VariableResolveAnalysis Analyze(
        RequestDefinition model,
        TreeNodeViewModel requestNode)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(requestNode);

        var session = new ResolveSession(requestNode, includeOrigin: false, MaxNestingDepth);
        foreach (var name in CollectReferencedNames(model))
        {
            session.ResolveName(name, depth: 1, chain: [], fallback: "{{" + name + "}}");
        }

        return new VariableResolveAnalysis(session.MissingList(), session.NestingErrors);
    }

    public static string Substitute(string? text, IReadOnlyDictionary<string, string> variables)
    {
        var spans = SubstituteWithSpans(text, variables);
        if (spans.Count == 0)
        {
            return string.Empty;
        }

        if (spans.Count == 1)
        {
            return spans[0].Text;
        }

        var sb = new StringBuilder();
        foreach (var span in spans)
        {
            sb.Append(span.Text);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Substitutes <c>{{name}}</c> tokens and records which output slices came from a resolved variable.
    /// Unresolved tokens are left as literal text and are not marked as from a variable.
    /// </summary>
    public static IReadOnlyList<ResolvedTextSpan> SubstituteWithSpans(
        string? text,
        IReadOnlyDictionary<string, string> variables)
    {
        text ??= string.Empty;
        if (text.Length == 0)
        {
            return [];
        }

        variables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var spans = new List<ResolvedTextSpan>();
        var last = 0;
        foreach (Match match in TokenRegex().Matches(text))
        {
            if (match.Index > last)
            {
                AppendSpan(spans, text[last..match.Index], fromVariable: false);
            }

            var name = match.Groups[1].Value.Trim();
            if (name.Length > 0 && variables.TryGetValue(name, out var value))
            {
                AppendSpan(spans, value, fromVariable: true);
            }
            else
            {
                AppendSpan(spans, match.Value, fromVariable: false);
            }

            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            AppendSpan(spans, text[last..], fromVariable: false);
        }

        return spans;
    }

    private static void AppendSpan(List<ResolvedTextSpan> spans, string text, bool fromVariable)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (spans.Count > 0 && spans[^1].IsFromVariable == fromVariable)
        {
            var previous = spans[^1];
            spans[^1] = previous with { Text = previous.Text + text };
            return;
        }

        spans.Add(new ResolvedTextSpan(text, fromVariable));
    }

    private static IReadOnlyDictionary<string, string> BuildResolvedScope(
        TreeNodeViewModel? origin,
        bool includeOrigin)
    {
        var session = new ResolveSession(origin, includeOrigin, MaxNestingDepth);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in session.CollectKeys())
        {
            map[key] = session.ResolveName(key, depth: 1, chain: [], fallback: "{{" + key + "}}");
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

    /// <summary>
    /// Token names at a click/caret, or those intersecting a selection.
    /// Empty when the pointer is not on a <c>{{name}}</c> token.
    /// </summary>
    public static IReadOnlyList<string> CollectTokenNames(
        string? text,
        int caretOrClick,
        int selectionStart,
        int selectionLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        if (selectionLength > 0)
        {
            var start = Math.Clamp(selectionStart, 0, text.Length);
            var length = Math.Clamp(selectionLength, 0, text.Length - start);
            var names = FindTokenNames(text.Substring(start, length));
            if (names.Count > 0)
            {
                return names;
            }

            if (TryFindTokenAt(text, start, out var selectedName, out _, out _))
            {
                return [selectedName];
            }

            return [];
        }

        return TryFindTokenAt(text, caretOrClick, out var name, out _, out _)
            ? [name]
            : [];
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

    private static bool ChainContains(List<string> chain, string name)
    {
        foreach (var item in chain)
        {
            if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ResolveSession
    {
        private readonly TreeNodeViewModel? _origin;
        private readonly bool _includeOrigin;
        private readonly int _maxDepth;
        private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _nestingReported = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<VariableNestingError> _nestingErrors = [];

        public ResolveSession(TreeNodeViewModel? origin, bool includeOrigin, int maxDepth)
        {
            _origin = origin;
            _includeOrigin = includeOrigin;
            _maxDepth = ClampMaxNestingDepth(maxDepth);
        }

        public IReadOnlyList<VariableNestingError> NestingErrors => _nestingErrors;

        public IReadOnlyList<string> MissingList() =>
            _missing.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        public IReadOnlyList<string> CollectKeys()
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var node = _includeOrigin ? _origin : _origin?.Parent;
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
                        if (seen.Add(key))
                        {
                            keys.Add(key);
                        }
                    }
                }

                node = node.Parent;
            }

            return keys;
        }

        public string ResolveName(string name, int depth, List<string> chain, string fallback)
        {
            var nextChain = AppendChain(chain, name);
            if (depth > _maxDepth || ChainContains(chain, name))
            {
                if (_nestingReported.Add(name))
                {
                    _nestingErrors.Add(new VariableNestingError(name, nextChain, _maxDepth));
                }

                return fallback;
            }

            if (_cache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var stored = FindStored(name);
            if (stored is null)
            {
                _missing.Add(name);
                return fallback;
            }

            var resolved = VariableValueRecipes.Resolve(stored);
            resolved = SubstituteNested(resolved, depth, nextChain);
            _cache[name] = resolved;
            return resolved;
        }

        private string SubstituteNested(string? text, int depth, List<string> chain)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            return TokenRegex().Replace(text, match =>
            {
                var nested = match.Groups[1].Value.Trim();
                if (nested.Length == 0)
                {
                    return match.Value;
                }

                return ResolveName(nested, depth + 1, chain, match.Value);
            });
        }

        private string? FindStored(string name)
        {
            var node = _includeOrigin ? _origin : _origin?.Parent;
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

                        if (string.Equals(item.Key.Trim(), name, StringComparison.OrdinalIgnoreCase))
                        {
                            return item.Value ?? string.Empty;
                        }
                    }
                }

                node = node.Parent;
            }

            return null;
        }

        private static List<string> AppendChain(List<string> chain, string name)
        {
            var next = new List<string>(chain.Count + 1);
            next.AddRange(chain);
            next.Add(name);
            return next;
        }
    }
}
