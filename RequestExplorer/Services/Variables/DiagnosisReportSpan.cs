using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

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
