using System.Text;
using RequestExplorer.Models;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

public sealed class DisabledVariableHint
{
    public DisabledVariableHint(string name, string location, TreeNodeViewModel? container)
    {
        Name = name;
        Location = location;
        Container = container;
    }

    public string Name { get; }

    /// <summary>Container name where the disabled variable is defined.</summary>
    public string Location { get; }

    public TreeNodeViewModel? Container { get; }
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

/// <summary>One run of text in a diagnosis report; optional <see cref="Node"/> makes it a link.</summary>
public sealed class DiagnosisReportSpan
{
    public DiagnosisReportSpan(string text, TreeNodeViewModel? node = null)
    {
        Text = text;
        Node = node;
    }

    public string Text { get; }

    public TreeNodeViewModel? Node { get; }
}

public sealed class DiagnosisReport
{
    public DiagnosisReport(IReadOnlyList<DiagnosisReportSpan> spans)
    {
        Spans = spans;
    }

    public IReadOnlyList<DiagnosisReportSpan> Spans { get; }

    public string ToPlainText()
    {
        var sb = new StringBuilder();
        foreach (var span in Spans)
        {
            sb.Append(span.Text);
        }

        return sb.ToString();
    }
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

        var hint = FormatDisabledHintPlain(diagnosis.DisabledHints);
        if (hint.Length > 0)
        {
            sb.Append(" (").Append(hint).Append(')');
        }

        return sb.ToString();
    }

    public static string FormatSubtreeReport(
        TreeNodeViewModel root,
        IReadOnlyList<RequestVariableDiagnosis> results) =>
        BuildSubtreeReport(root, results).ToPlainText();

    public static DiagnosisReport BuildSubtreeReport(
        TreeNodeViewModel root,
        IReadOnlyList<RequestVariableDiagnosis> results)
    {
        var spans = new List<DiagnosisReportSpan>();

        AppendLink(spans, root.HierarchyPath, root);

        if (results.Count == 0)
        {
            Append(spans, "\nNo requests under this node.");
            return new DiagnosisReport(spans);
        }

        var failing = results.Where(r => r.HasIssues).ToList();
        Append(spans, " — ");
        if (failing.Count == 0)
        {
            Append(spans, "OK (");
            Append(spans, results.Count.ToString());
            Append(spans, results.Count == 1 ? " request)" : " requests)");
            return new DiagnosisReport(spans);
        }

        Append(spans, failing.Count.ToString());
        Append(spans, "/");
        Append(spans, results.Count.ToString());
        Append(spans, results.Count == 1 ? " request missing variables" : " requests missing variables");
        Append(spans, "\n");

        IReadOnlyList<TreeNodeViewModel>? previousSegments = null;
        foreach (var item in failing)
        {
            var segments = GetRelativePathNodes(root, item.Request);
            var common = CountCommonPrefix(previousSegments, segments);

            Append(spans, "\n");

            for (var i = common; i < segments.Count - 1; i++)
            {
                var folder = segments[i];
                Append(spans, Indent(i));
                AppendLink(spans, folder.Name, folder);
                Append(spans, "\n");
            }

            var leafDepth = Math.Max(0, segments.Count - 1);
            var leaf = segments.Count > 0 ? segments[^1] : item.Request;
            Append(spans, Indent(leafDepth));
            Append(spans, item.Request.Verb.ToString());
            Append(spans, " ");
            AppendLink(spans, leaf.Name, leaf);
            Append(spans, "\n");

            var detailIndent = Indent(leafDepth + 1);
            Append(spans, detailIndent);
            Append(spans, FormatMissingList(item.MissingVariables));

            if (item.DisabledHints.Count > 0)
            {
                Append(spans, "\n");
                Append(spans, detailIndent);
                AppendDisabledHints(spans, item.DisabledHints);
            }

            previousSegments = segments;
        }

        return new DiagnosisReport(spans);
    }

    private static void Append(List<DiagnosisReportSpan> spans, string text) =>
        spans.Add(new DiagnosisReportSpan(text));

    private static void AppendLink(List<DiagnosisReportSpan> spans, string text, TreeNodeViewModel node) =>
        spans.Add(new DiagnosisReportSpan(text, node));

    private static void AppendDisabledHints(
        List<DiagnosisReportSpan> spans,
        IReadOnlyList<DisabledVariableHint> hints)
    {
        var groups = hints
            .GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var g = 0; g < groups.Count; g++)
        {
            if (g > 0)
            {
                Append(spans, "; ");
            }

            var group = groups[g];
            Append(spans, "{{");
            Append(spans, group.Key);
            Append(spans, "}} disabled on ");

            var locations = group
                .GroupBy(h => h.Container ?? (object)h.Location)
                .Select(x => x.First())
                .ToList();

            for (var i = 0; i < locations.Count; i++)
            {
                if (i > 0)
                {
                    Append(spans, ", ");
                }

                var hint = locations[i];
                if (hint.Container is not null)
                {
                    AppendLink(spans, hint.Location, hint.Container);
                }
                else
                {
                    Append(spans, hint.Location);
                }
            }
        }
    }

    private static IReadOnlyList<TreeNodeViewModel> GetRelativePathNodes(
        TreeNodeViewModel root,
        TreeNodeViewModel node)
    {
        var parts = new List<TreeNodeViewModel>();
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                if (parts.Count == 0)
                {
                    parts.Add(node);
                }

                break;
            }

            parts.Add(current);
            if (current.Parent is null)
            {
                break;
            }
        }

        parts.Reverse();
        return parts;
    }

    private static int CountCommonPrefix(
        IReadOnlyList<TreeNodeViewModel>? previous,
        IReadOnlyList<TreeNodeViewModel> current)
    {
        if (previous is null || previous.Count == 0)
        {
            return 0;
        }

        // Previous leaf is always a request — never treat it as a shared folder.
        var max = Math.Min(current.Count, previous.Count - 1);
        var i = 0;
        while (i < max && ReferenceEquals(previous[i], current[i]))
        {
            i++;
        }

        return i;
    }

    private static string Indent(int depth) => depth <= 0 ? string.Empty : new string(' ', depth * 2);

    private static string FormatMissingList(IReadOnlyList<string> names) =>
        string.Join(", ", names.Select(n => "{{" + n + "}}"));

    private static string FormatDisabledHintPlain(IReadOnlyList<DisabledVariableHint> hints)
    {
        if (hints.Count == 0)
        {
            return string.Empty;
        }

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

                var dedupe = key + "\0" + node.Name;
                if (!seen.Add(dedupe))
                {
                    continue;
                }

                hints.Add(new DisabledVariableHint(key, node.Name, node));
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
