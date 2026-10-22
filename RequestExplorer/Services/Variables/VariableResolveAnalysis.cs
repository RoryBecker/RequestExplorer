namespace RequestExplorer.Services.Variables;

public sealed class VariableResolveAnalysis
{
    public VariableResolveAnalysis(
        IReadOnlyList<string> missing,
        IReadOnlyList<VariableNestingError> nestingErrors)
    {
        Missing = missing;
        NestingErrors = nestingErrors;
    }

    public IReadOnlyList<string> Missing { get; }

    public IReadOnlyList<VariableNestingError> NestingErrors { get; }
}
