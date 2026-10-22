namespace RequestExplorer.Services.Variables;

public sealed class VariableNestingError
{
    public VariableNestingError(string variableName, IReadOnlyList<string> chain, int maxDepth)
    {
        VariableName = variableName;
        Chain = chain;
        MaxDepth = maxDepth;
    }

    public string VariableName { get; }

    public IReadOnlyList<string> Chain { get; }

    public int MaxDepth { get; }

    public string Format()
    {
        var path = Chain.Count == 0
            ? "{{" + VariableName + "}}"
            : string.Join(" → ", Chain.Select(n => "{{" + n + "}}"));
        return $"nested too deeply: {path} (max {MaxDepth})";
    }
}
