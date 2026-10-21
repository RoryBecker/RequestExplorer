using System.Text;

namespace RequestExplorer.Services.Variables;

public sealed class DiagnosisReport
{
    public DiagnosisReport(IReadOnlyList<DiagnosisReportSpan> spans)
    {
        Spans = spans;
    }

    public IReadOnlyList<DiagnosisReportSpan> Spans { get; }

    public string ToPlainText()
    {
        var sb = new StringBuilder();
        foreach (var span in Spans)
        {
            sb.Append(span.Text);
        }

        return sb.ToString();
    }
}
