using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

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
