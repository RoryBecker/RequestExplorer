using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

public sealed class PostmanHoistClash
{
    public PostmanHoistClash(string name, IReadOnlyList<PostmanHoistClashValue> values)
    {
        Name = name;
        Values = values;
    }

    public string Name { get; }

    public IReadOnlyList<PostmanHoistClashValue> Values { get; }
}

public sealed class PostmanHoistClashValue
{
    public PostmanHoistClashValue(
        string value,
        IReadOnlyList<TreeNodeViewModel> definedOn,
        bool isSecret)
    {
        Value = value;
        DefinedOn = definedOn;
        IsSecret = isSecret;
    }

    public string Value { get; }

    public IReadOnlyList<TreeNodeViewModel> DefinedOn { get; }

    public bool IsSecret { get; }
}

/// <summary>
/// Postman collection variables live at the document root. Folder overrides along one
/// path are fine; the same enabled name with different values on different request
/// branches cannot be lifted without losing a value.
/// </summary>
public static class PostmanVariableHoistClash
{
    private const int ValuePreviewMaxChars = 80;

    public static IReadOnlyList<PostmanHoistClash> Find(TreeNodeViewModel root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root is RequestNodeViewModel)
        {
            return [];
        }

        var byName = new Dictionary<string, Dictionary<string, ValueBucket>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var request in EnumerateRequests(root))
        {
            foreach (var (name, value, definedOn, isSecret) in EffectiveDefinitions(request, root))
            {
                if (!byName.TryGetValue(name, out var byValue))
                {
                    byValue = new Dictionary<string, ValueBucket>(StringComparer.Ordinal);
                    byName[name] = byValue;
                }

                if (!byValue.TryGetValue(value, out var bucket))
                {
                    bucket = new ValueBucket();
                    byValue[value] = bucket;
                }

                bucket.Nodes.Add(definedOn);
                bucket.IsSecret |= isSecret;
            }
        }

        var clashes = new List<PostmanHoistClash>();
        foreach (var (name, byValue) in byName.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (byValue.Count < 2)
            {
                continue;
            }

            var values = byValue
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new PostmanHoistClashValue(
                    kv.Key,
                    kv.Value.Nodes
                        .OrderBy(n => n.HierarchyPath, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    kv.Value.IsSecret))
                .ToList();

            clashes.Add(new PostmanHoistClash(name, values));
        }

        return clashes;
    }

    public static DiagnosisReport BuildReport(
        TreeNodeViewModel root,
        IReadOnlyList<PostmanHoistClash> clashes)
    {
        var spans = new List<DiagnosisReportSpan>();
        Append(spans, "Cannot export ");
        AppendLink(spans, root.HierarchyPath, root);
        Append(spans, " to Postman.\n\n");
        Append(
            spans,
            "Postman only stores variables at the collection root. Folder variables would have to be lifted there, and these names have different values on different branches.\n");

        foreach (var clash in clashes)
        {
            Append(spans, "\n{{");
            Append(spans, clash.Name);
            Append(spans, "}}\n");

            foreach (var entry in clash.Values)
            {
                Append(spans, "  ");
                Append(spans, FormatValue(entry));
                Append(spans, " on ");

                for (var i = 0; i < entry.DefinedOn.Count; i++)
                {
                    if (i > 0)
                    {
                        Append(spans, ", ");
                    }

                    var node = entry.DefinedOn[i];
                    AppendLink(spans, node.Name, node);
                }

                Append(spans, "\n");
            }
        }

        Append(
            spans,
            "\nExport to Postman (Flatten) still works — it inlines each request's resolved values instead of lifting variables.");

        return new DiagnosisReport(spans);
    }

    private static IEnumerable<(string Name, string Value, TreeNodeViewModel DefinedOn, bool IsSecret)> EffectiveDefinitions(
        RequestNodeViewModel request,
        TreeNodeViewModel root)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var node = request.Parent; node is not null; node = node.Parent)
        {
            if (node is ContainerNodeViewModel container)
            {
                foreach (var item in container.Variables)
                {
                    if (!item.IsEnabled || !item.HasKey)
                    {
                        continue;
                    }

                    var key = item.Key.Trim();
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    yield return (key, item.Value ?? string.Empty, container, item.IsSecret);
                }
            }

            if (ReferenceEquals(node, root))
            {
                yield break;
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

    private static string FormatValue(PostmanHoistClashValue entry)
    {
        if (entry.IsSecret)
        {
            return "(secret)";
        }

        var preview = entry.Value.Replace('\r', ' ').Replace('\n', ' ');
        if (preview.Length > ValuePreviewMaxChars)
        {
            preview = preview[..ValuePreviewMaxChars] + "…";
        }

        return "\"" + preview + "\"";
    }

    private sealed class ValueBucket
    {
        public HashSet<TreeNodeViewModel> Nodes { get; } = [];

        public bool IsSecret { get; set; }
    }

    private static void Append(List<DiagnosisReportSpan> spans, string text) =>
        spans.Add(new DiagnosisReportSpan(text));

    private static void AppendLink(List<DiagnosisReportSpan> spans, string text, TreeNodeViewModel node) =>
        spans.Add(new DiagnosisReportSpan(text, node));
}
