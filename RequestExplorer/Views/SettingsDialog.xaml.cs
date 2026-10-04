using System.Windows;
using RequestExplorer.Services;

namespace RequestExplorer.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(UiPreferences preferences, string? backupsDirectory = null)
    {
        InitializeComponent();
        Preferences = preferences;
        DevModeCheck.IsChecked = preferences.DevModeIgnoreTimeout;
        TimeoutBox.Text = Math.Clamp(preferences.RequestTimeoutSeconds, 1, 3600).ToString();
        BackupOnStartupCheck.IsChecked = preferences.BackupOnStartup;
        RetentionBox.Text = WorkspaceBackupService.ClampRetention(preferences.BackupRetentionCount).ToString();
        BackupsPathBlock.Text = string.IsNullOrWhiteSpace(backupsDirectory)
            ? string.Empty
            : $"Folder:\n{backupsDirectory}";
        UpdateTimeoutEnabled();
    }

    public UiPreferences Preferences { get; }

    public bool DevModeIgnoreTimeout => DevModeCheck.IsChecked == true;

    public int RequestTimeoutSeconds { get; private set; } = 30;

    public bool BackupOnStartup => BackupOnStartupCheck.IsChecked == true;

    public int BackupRetentionCount { get; private set; } = WorkspaceBackupService.DefaultRetention;

    private void DevModeCheck_OnChanged(object sender, RoutedEventArgs e) => UpdateTimeoutEnabled();

    private void UpdateTimeoutEnabled() =>
        TimeoutBox.IsEnabled = DevModeCheck.IsChecked != true;

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TimeoutBox.Text.Trim(), out var parsed) && parsed is >= 1 and <= 3600)
        {
            RequestTimeoutSeconds = parsed;
        }
        else if (DevModeIgnoreTimeout)
        {
            RequestTimeoutSeconds = Math.Clamp(Preferences.RequestTimeoutSeconds, 1, 3600);
            if (RequestTimeoutSeconds < 1)
            {
                RequestTimeoutSeconds = 30;
            }
        }
        else
        {
            MessageBox.Show(
                this,
                "Enter a timeout between 1 and 3600 seconds.",
                "Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (int.TryParse(RetentionBox.Text.Trim(), out var retention)
            && retention is >= WorkspaceBackupService.MinRetention and <= WorkspaceBackupService.MaxRetention)
        {
            BackupRetentionCount = retention;
        }
        else
        {
            MessageBox.Show(
                this,
                $"Enter a backup retention count between {WorkspaceBackupService.MinRetention} and {WorkspaceBackupService.MaxRetention}.",
                "Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
