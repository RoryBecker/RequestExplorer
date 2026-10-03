using System.Windows;
using RequestExplorer.Services;

namespace RequestExplorer.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(UiPreferences preferences)
    {
        InitializeComponent();
        Preferences = preferences;
        DevModeCheck.IsChecked = preferences.DevModeIgnoreTimeout;
        TimeoutBox.Text = Math.Clamp(preferences.RequestTimeoutSeconds, 1, 3600).ToString();
        UpdateTimeoutEnabled();
    }

    public UiPreferences Preferences { get; }

    public bool DevModeIgnoreTimeout => DevModeCheck.IsChecked == true;

    public int RequestTimeoutSeconds { get; private set; } = 30;

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

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
