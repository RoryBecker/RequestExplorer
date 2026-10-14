using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RequestExplorer.Services.Themes;

/// <summary>Applies Windows immersive dark/light title-bar chrome to WPF windows.</summary>
public static class WindowTitleBarTheme
{
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    private static bool _classHandlerRegistered;

    public static void Register()
    {
        if (_classHandlerRegistered)
        {
            return;
        }

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
        _classHandlerRegistered = true;
    }

    public static void ApplyToAllWindows(bool isDark)
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (Window window in Application.Current.Windows)
        {
            Apply(window, isDark);
        }
    }

    public static void Apply(Window window, bool isDark)
    {
        ArgumentNullException.ThrowIfNull(window);

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var useDark = isDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref useDark, sizeof(int));
        }

        // Windows 11: tint caption to match the app panel; ignored on older builds.
        var caption = isDark ? DarkCaption : LightCaption;
        DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(uint));

        var text = isDark ? DarkCaptionText : LightCaptionText;
        DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(uint));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            Apply(window, ThemeService.IsDark);
        }
    }

    // COLORREF is 0x00BBGGRR (matches App.PanelBrush / App.TextBrush).
    private static readonly uint DarkCaption = PackColorRef(34, 38, 46);
    private static readonly uint LightCaption = PackColorRef(255, 255, 255);
    private static readonly uint DarkCaptionText = PackColorRef(229, 231, 235);
    private static readonly uint LightCaptionText = PackColorRef(31, 41, 55);

    private static uint PackColorRef(byte r, byte g, byte b) =>
        (uint)(r | (g << 8) | (b << 16));

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref int attrValue,
        int attrSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref uint attrValue,
        int attrSize);
}
