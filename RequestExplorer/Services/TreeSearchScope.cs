namespace RequestExplorer.Services;

/// <summary>Which parts of a node contribute to <c>SearchText</c> for tree filtering.</summary>
public readonly record struct TreeSearchScope(bool IncludeAncestors, bool IncludeName, bool IncludeUrl)
{
    public static TreeSearchScope All { get; } = new(true, true, true);

    public int EnabledCount =>
        (IncludeAncestors ? 1 : 0) + (IncludeName ? 1 : 0) + (IncludeUrl ? 1 : 0);
}
