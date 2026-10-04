using System.Windows;
using System.Windows.Input;
using RequestExplorer.Services;

namespace RequestExplorer.Views;

public partial class RestoreBackupDialog : Window
{
    public RestoreBackupDialog(IReadOnlyList<BackupListItem> backups)
    {
        InitializeComponent();
        BackupList.ItemsSource = backups;
        EmptyMessage.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreButton.IsEnabled = backups.Count > 0;
        if (backups.Count > 0)
        {
            BackupList.SelectedIndex = 0;
        }
    }

    public BackupListItem? SelectedBackup { get; private set; }

    private void Restore_OnClick(object sender, RoutedEventArgs e) => TryAccept();

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void BackupList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => TryAccept();

    private void TryAccept()
    {
        if (BackupList.SelectedItem is not BackupListItem item)
        {
            MessageBox.Show(
                this,
                "Select a backup to restore.",
                "Restore",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SelectedBackup = item;
        DialogResult = true;
    }
}
