using System.Windows;
using RequestExplorer.Services;

namespace RequestExplorer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        EscapeCancel.Register();
        WindowTitleBarTheme.Register();
    }
}
