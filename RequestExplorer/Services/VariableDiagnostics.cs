using System.Text;
using RequestExplorer.Models;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Services;

public sealed class RequestVariableDiagnosis
{
    public RequestVariableDiagnosis(RequestNodeViewModel request, IReadOnlyList<string> missingVariables)
    {
        Request = request;
        MissingVariables = missingVariables;
    }

    public RequestNodeViewModel Request { get; }

    public IReadOnlyList<string> MissingVariables { get; }

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

    public static RequestVariableDiagnosis DiagnoseRequest(RequestNodeViewModel request) =>
        new(request, FindMissing(request.Model, request));

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

        var names = string.Join(", ", diagnosis.MissingVariables);
        return $"Missing variable(s): {names}";
    }

    public static string FormatSubtreeReport(
        TreeNodeViewModel root,
        IReadOnlyList<RequestVariableDiagnosis> results)
    {
        var sb = new StringBuilder();
        var kind = root.Kind switch
        {
            TreeNodeKind.Collection => "Collection",
            TreeNodeKind.Folder => "Folder",
            TreeNodeKind.Request => "Request",
            _ => "Node"
        };

        sb.Append("Variable diagnosis — ").Append(kind).Append(" '").Append(root.Name).Append('\'');
        sb.AppendLine();
        sb.AppendLine();

        if (results.Count == 0)
        {
            sb.Append("No requests found under this node.");
            return sb.ToString();
        }

        var failing = results.Where(r => r.HasIssues).ToList();
        if (failing.Count == 0)
        {
            sb.Append("All ").Append(results.Count).Append(" request(s) resolve successfully.");
            return sb.ToString();
        }

        sb.Append(failing.Count).Append(" of ").Append(results.Count)
          .Append(" request(s) have missing variables.");
        sb.AppendLine();
        sb.AppendLine();

        foreach (var item in failing)
        {
            sb.Append(item.Request.Verb).Append(' ').Append(item.Request.Name);
            sb.AppendLine();
            sb.Append("  Missing: ").Append(string.Join(", ", item.MissingVariables));
            sb.AppendLine();
            sb.AppendLine();
        }

        var ok = results.Count - failing.Count;
        if (ok > 0)
        {
            sb.Append(ok).Append(" request(s) OK.");
        }

        return sb.ToString().TrimEnd();
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
