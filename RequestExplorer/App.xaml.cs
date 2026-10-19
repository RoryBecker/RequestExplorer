using System.Windows;
using System.Windows.Controls;
using RequestExplorer.Services.Themes;

namespace RequestExplorer;

public partial class App : Application
{
    /// <summary>WPF default <see cref="ToolTipService.InitialShowDelay"/> is 400ms.</summary>
    private const int DefaultTooltipShowDelayMs = 400;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(DefaultTooltipShowDelayMs / 2));
        EscapeCancel.Register();
        WindowTitleBarTheme.Register();
    }
}
