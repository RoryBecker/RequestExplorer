using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

/// <summary>One run of text in a diagnosis report; optional <see cref="Node"/> makes it a link.</summary>
public sealed class DiagnosisReportSpan
{
    public DiagnosisReportSpan(
        string text,
        TreeNodeViewModel? node = null,
        TreeNodeViewModel? variableFrom = null)
    {
        Text = text;
        Node = node;
        VariableFrom = variableFrom;
    }

    public string Text { get; }

    public TreeNodeViewModel? Node { get; }

    /// <summary>When set, <c>{{tokens}}</c> in <see cref="Text"/> get Define / Go to Definition.</summary>
    public TreeNodeViewModel? VariableFrom { get; }
}
