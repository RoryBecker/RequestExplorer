using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace RequestExplorer.Views;

public partial class AboutDialog : Window
{
    public AboutDialog(string storageDirectory, string backupsDirectory)
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        VersionBlock.Text = $"Version {version} · .NET 8 WPF";
        FillFolderBlock(StorageBlock, "Collections folder:", storageDirectory);
        FillFolderBlock(BackupsBlock, "Backups folder:", backupsDirectory);
    }

    private void FillFolderBlock(TextBlock block, string label, string? directory)
    {
        var path = directory ?? string.Empty;
        block.Inlines.Clear();
        block.Inlines.Add(new Run(label + "\n"));
        if (string.IsNullOrWhiteSpace(path))
        {
            block.Inlines.Add(new Run("(not set)"));
            return;
        }

        var link = new Hyperlink(new Run(path))
        {
            Cursor = Cursors.Hand,
            ToolTip = "Open folder",
            TextDecorations = TextDecorations.Underline,
            Tag = path
        };
        if (TryFindResource("App.AccentBrush") is Brush accent)
        {
            link.Foreground = accent;
        }

        link.Click += FolderLink_OnClick;
        block.Inlines.Add(link);
    }

    private void FolderLink_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Hyperlink { Tag: string path } || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Open Folder Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
