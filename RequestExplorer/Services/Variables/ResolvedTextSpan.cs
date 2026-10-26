namespace RequestExplorer.Services.Variables;

/// <summary>A slice of substituted text and whether it came from a <c>{{variable}}</c>.</summary>
public readonly record struct ResolvedTextSpan(string Text, bool IsFromVariable);
