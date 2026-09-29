using System.Reflection;
using System.Windows;

namespace RequestExplorer.Views;

public partial class AboutDialog : Window
{
    public AboutDialog(string storageDirectory)
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        VersionBlock.Text = $"Version {version} · .NET 8 WPF";
        StorageBlock.Text = $"Collections folder:\n{storageDirectory}";
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
