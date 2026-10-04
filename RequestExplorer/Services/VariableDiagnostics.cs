using System.Text;
using RequestExplorer.Models;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Services;

public sealed class DisabledVariableHint
{
    public DisabledVariableHint(string name, string location)
    {
        Name = name;
        Location = location;
    }

    public string Name { get; }

    /// <summary>Container name where the disabled variable is defined.</summary>
    public string Location { get; }
}

public sealed class RequestVariableDiagnosis
{
    public RequestVariableDiagnosis(
        RequestNodeViewModel request,
        IReadOnlyList<string> missingVariables,
        IReadOnlyList<DisabledVariableHint> disabledHints)
    {
        Request = request;
        MissingVariables = missingVariables;
        DisabledHints = disabledHints;
    }

    public RequestNodeViewModel Request { get; }

    public IReadOnlyList<string> MissingVariables { get; }

    public IReadOnlyList<DisabledVariableHint> DisabledHints { get; }

    public bool HasIssues => MissingVariables.Count > 0;
}

public static class VariableDiagnostics
{
    public static IReadOnlyList<string> FindMissing(
        RequestDefinition model,
        TreeNodeViewModel requestNode)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(requestNode);

        var scope = VariableResolver.BuildScope(requestNode);
        var referenced = VariableResolver.CollectReferencedNames(model);
        return referenced
            .Where(name => !scope.ContainsKey(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static RequestVariableDiagnosis DiagnoseRequest(RequestNodeViewModel request)
    {
        var missing = FindMissing(request.Model, request);
        var disabled = FindDisabledHints(request, missing);
        return new RequestVariableDiagnosis(request, missing, disabled);
    }

    public static IReadOnlyList<RequestVariableDiagnosis> DiagnoseSubtree(TreeNodeViewModel root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var results = new List<RequestVariableDiagnosis>();
        Collect(root, results);
        return results;
    }

    public static string FormatRequestReport(RequestVariableDiagnosis diagnosis)
    {
        if (!diagnosis.HasIssues)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append(diagnosis.Request.HierarchyPath);
        sb.Append(" — missing ");
        sb.Append(FormatMissingList(diagnosis.MissingVariables));

        var hint = FormatDisabledHintSuffix(diagnosis.DisabledHints);
        if (hint.Length > 0)
        {
            sb.Append(" (").Append(hint).Append(')');
        }

        return sb.ToString();
    }

    public static string FormatSubtreeReport(
        TreeNodeViewModel root,
        IReadOnlyList<RequestVariableDiagnosis> results)
    {
        var sb = new StringBuilder();
        sb.Append(root.HierarchyPath);

        if (results.Count == 0)
        {
            sb.AppendLine();
            sb.Append("No requests under this node.");
            return sb.ToString();
        }

        var failing = results.Where(r => r.HasIssues).ToList();
        sb.Append(" — ");
        if (failing.Count == 0)
        {
            sb.Append("OK (").Append(results.Count).Append(" request");
            if (results.Count != 1)
            {
                sb.Append('s');
            }

            sb.Append(')');
            return sb.ToString();
        }

        sb.Append(failing.Count).Append('/').Append(results.Count)
          .Append(" request");
        if (results.Count != 1)
        {
            sb.Append('s');
        }

        sb.Append(" missing variables");
        sb.AppendLine();

        IReadOnlyList<string>? previousSegments = null;
        foreach (var item in failing)
        {
            var segments = GetRelativePathSegments(root, item.Request);
            var common = CountCommonPrefix(previousSegments, segments);

            sb.AppendLine();

            // Emit only the path parts that differ from the previous entry.
            for (var i = common; i < segments.Count - 1; i++)
            {
                sb.Append(Indent(i)).Append(segments[i]).AppendLine();
            }

            var leafDepth = Math.Max(0, segments.Count - 1);
            var leafName = segments.Count > 0 ? segments[^1] : item.Request.Name;
            sb.Append(Indent(leafDepth))
              .Append(item.Request.Verb)
              .Append(' ')
              .Append(leafName)
              .AppendLine();

            var detailIndent = Indent(leafDepth + 1);
            sb.Append(detailIndent).Append(FormatMissingList(item.MissingVariables));

            var hint = FormatDisabledHintSuffix(item.DisabledHints);
            if (hint.Length > 0)
            {
                sb.AppendLine();
                sb.Append(detailIndent).Append(hint);
            }

            previousSegments = segments;
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> GetRelativePathSegments(
        TreeNodeViewModel root,
        TreeNodeViewModel node)
    {
        var parts = new List<string>();
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                // Diagnosing a single request: keep its name so the leaf still appears.
                if (parts.Count == 0)
                {
                    parts.Add(node.Name);
                }

                break;
            }

            parts.Add(current.Name);
            if (current.Parent is null)
            {
                break;
            }
        }

        parts.Reverse();
        return parts;
    }

    private static int CountCommonPrefix(
        IReadOnlyList<string>? previous,
        IReadOnlyList<string> current)
    {
        if (previous is null || previous.Count == 0)
        {
            return 0;
        }

        // Previous leaf is always a request name — never treat it as a shared folder.
        var max = Math.Min(current.Count, previous.Count - 1);
        var i = 0;
        while (i < max && string.Equals(previous[i], current[i], StringComparison.OrdinalIgnoreCase))
        {
            i++;
        }

        return i;
    }

    private static string Indent(int depth) => depth <= 0 ? string.Empty : new string(' ', depth * 2);

    private static string FormatMissingList(IReadOnlyList<string> names) =>
        string.Join(", ", names.Select(n => "{{" + n + "}}"));

    private static string FormatDisabledHintSuffix(IReadOnlyList<DisabledVariableHint> hints)
    {
        if (hints.Count == 0)
        {
            return string.Empty;
        }

        // Collapse duplicates: name@location
        var parts = hints
            .GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var locations = string.Join(", ", g.Select(h => h.Location).Distinct(StringComparer.OrdinalIgnoreCase));
                return $"{{{{{g.Key}}}}} disabled on {locations}";
            });

        return string.Join("; ", parts);
    }

    private static IReadOnlyList<DisabledVariableHint> FindDisabledHints(
        RequestNodeViewModel request,
        IReadOnlyList<string> missing)
    {
        if (missing.Count == 0)
        {
            return [];
        }

        var missingSet = new HashSet<string>(missing, StringComparer.OrdinalIgnoreCase);
        var hints = new List<DisabledVariableHint>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var node = request.Parent; node is not null; node = node.Parent)
        {
            if (node is not ContainerNodeViewModel container)
            {
                continue;
            }

            foreach (var item in container.Variables)
            {
                if (item.IsEnabled || string.IsNullOrWhiteSpace(item.Key))
                {
                    continue;
                }

                var key = item.Key.Trim();
                if (!missingSet.Contains(key))
                {
                    continue;
                }

                // Nearest ancestor definition wins for the hint.
                var dedupe = key + "\0" + node.Name;
                if (!seen.Add(dedupe))
                {
                    continue;
                }

                hints.Add(new DisabledVariableHint(key, node.Name));
            }
        }

        return hints
            .OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Location, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Collect(TreeNodeViewModel node, List<RequestVariableDiagnosis> results)
    {
        if (node is RequestNodeViewModel request)
        {
            results.Add(DiagnoseRequest(request));
            return;
        }

        foreach (var child in node.Children)
        {
            Collect(child, results);
        }
    }
}
