using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RequestExplorer.Services.Variables;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Views;

public partial class DiagnosisReportDialog : Window
{
    private static readonly Brush LinkBrush = CreateLinkBrush();

    private static Brush CreateLinkBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));
        brush.Freeze();
        return brush;
    }

    private readonly Action<TreeNodeViewModel>? _navigateToNode;

    public DiagnosisReportDialog(
        string title,
        DiagnosisReport report,
        Action<TreeNodeViewModel>? navigateToNode = null)
    {
        InitializeComponent();
        Title = title;
        _navigateToNode = navigateToNode;
        ReportBox.Document = BuildDocument(report);
    }

    /// <summary>Node chosen via a report link; set before the dialog closes.</summary>
    public TreeNodeViewModel? NavigatedNode { get; private set; }

    private FlowDocument BuildDocument(DiagnosisReport report)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            FontFamily = ReportBox.FontFamily,
            FontSize = ReportBox.FontSize
        };

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = 18
        };

        foreach (var span in report.Spans)
        {
            if (span.Text.Length == 0)
            {
                continue;
            }

            // Split on newlines so each line stays in the same paragraph with LineBreaks.
            var parts = span.Text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    paragraph.Inlines.Add(new LineBreak());
                }

                var piece = parts[i];
                if (piece.Length == 0)
                {
                    continue;
                }

                if (span.Node is not null)
                {
                    paragraph.Inlines.Add(CreateNodeLink(piece, span.Node));
                }
                else
                {
                    paragraph.Inlines.Add(new Run(piece));
                }
            }
        }

        document.Blocks.Add(paragraph);
        return document;
    }

    private Hyperlink CreateNodeLink(string text, TreeNodeViewModel node)
    {
        var link = new Hyperlink(new Run(text))
        {
            Tag = node,
            Cursor = Cursors.Hand,
            ToolTip = $"Go to {node.HierarchyPath}",
            Foreground = LinkBrush,
            TextDecorations = TextDecorations.Underline
        };
        link.Click += NodeLink_OnClick;
        return link;
    }

    private void NodeLink_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink { Tag: TreeNodeViewModel node })
        {
            return;
        }

        e.Handled = true;
        NavigatedNode = node;
        _navigateToNode?.Invoke(node);
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
