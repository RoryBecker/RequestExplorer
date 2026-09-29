using System.Windows;

namespace RequestExplorer.Views;

public partial class DiagnosisReportDialog : Window
{
    public DiagnosisReportDialog(string title, string report)
    {
        InitializeComponent();
        Title = title;
        ReportBox.Text = report;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
