using System.Windows;
using RequestExplorer.Services.Backup;
using RequestExplorer.Services.Preferences;
using RequestExplorer.Services.Variables;

namespace RequestExplorer.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(UiPreferences preferences, string? backupsDirectory = null)
    {
        InitializeComponent();
        Preferences = preferences;
        DevModeCheck.IsChecked = preferences.DevModeIgnoreTimeout;
        TimeoutBox.Text = Math.Clamp(preferences.RequestTimeoutSeconds, 1, 3600).ToString();
        NestingDepthBox.Text = VariableResolver.ClampMaxNestingDepth(preferences.VariableNestingMaxDepth).ToString();
        MouseTreeNavCheck.IsChecked = preferences.EnableMouseTreeNavigation;
        BackupOnStartupCheck.IsChecked = preferences.BackupOnStartup;
        RetentionBox.Text = BackupService.ClampRetention(preferences.BackupRetentionCount).ToString();
        BackupsPathBlock.Text = string.IsNullOrWhiteSpace(backupsDirectory)
            ? string.Empty
            : $"Folder:\n{backupsDirectory}";
        UpdateTimeoutEnabled();
    }

    public UiPreferences Preferences { get; }

    public bool DevModeIgnoreTimeout => DevModeCheck.IsChecked == true;

    public int RequestTimeoutSeconds { get; private set; } = 30;

    public int VariableNestingMaxDepth { get; private set; } = VariableResolver.DefaultMaxNestingDepth;

    public bool EnableMouseTreeNavigation => MouseTreeNavCheck.IsChecked == true;

    public bool BackupOnStartup => BackupOnStartupCheck.IsChecked == true;

    public int BackupRetentionCount { get; private set; } = BackupService.DefaultRetention;

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

        if (int.TryParse(NestingDepthBox.Text.Trim(), out var nesting)
            && nesting is >= VariableResolver.MinMaxNestingDepth and <= VariableResolver.MaxMaxNestingDepth)
        {
            VariableNestingMaxDepth = nesting;
        }
        else
        {
            MessageBox.Show(
                this,
                $"Enter a variable nesting depth between {VariableResolver.MinMaxNestingDepth} and {VariableResolver.MaxMaxNestingDepth}.",
                "Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (int.TryParse(RetentionBox.Text.Trim(), out var retention)
            && retention is >= BackupService.MinRetention and <= BackupService.MaxRetention)
        {
            BackupRetentionCount = retention;
        }
        else
        {
            MessageBox.Show(
                this,
                $"Enter a backup retention count between {BackupService.MinRetention} and {BackupService.MaxRetention}.",
                "Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
