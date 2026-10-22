using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

public sealed class RequestVariableDiagnosis
{
    public RequestVariableDiagnosis(
        RequestNodeViewModel request,
        IReadOnlyList<string> missingVariables,
        IReadOnlyList<DisabledVariableHint> disabledHints,
        IReadOnlyList<VariableNestingError>? nestingErrors = null)
    {
        Request = request;
        MissingVariables = missingVariables;
        DisabledHints = disabledHints;
        NestingErrors = nestingErrors ?? [];
    }

    public RequestNodeViewModel Request { get; }

    public IReadOnlyList<string> MissingVariables { get; }

    public IReadOnlyList<DisabledVariableHint> DisabledHints { get; }

    public IReadOnlyList<VariableNestingError> NestingErrors { get; }

    public bool HasIssues => MissingVariables.Count > 0 || NestingErrors.Count > 0;
}
