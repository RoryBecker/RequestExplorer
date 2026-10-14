using System.Windows;
using System.Windows.Media;

namespace RequestExplorer.Services.Themes;

/// <summary>Applies light/dark colors to shared <c>App.*</c> brushes in application resources.</summary>
public static class ThemeService
{
    private static bool? _appliedDark;

    public static bool IsDark => _appliedDark == true;

    public static void Apply(bool isDark)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        // Always re-apply so toggles replace brush instances and DynamicResource bindings refresh.
        _appliedDark = isDark;
        var colors = isDark ? Dark : Light;

        foreach (var (key, color) in colors)
        {
            SetBrush(resources, key, color);
        }

        // Keep common system chrome in sync (menus, combo boxes, list defaults).
        SetSystemBrush(resources, SystemColors.WindowBrushKey, colors["App.PanelBrush"]);
        SetSystemBrush(resources, SystemColors.WindowTextBrushKey, colors["App.TextBrush"]);
        SetSystemBrush(resources, SystemColors.ControlBrushKey, colors["App.ControlBackgroundBrush"]);
        SetSystemBrush(resources, SystemColors.ControlTextBrushKey, colors["App.TextBrush"]);
        SetSystemBrush(resources, SystemColors.ControlLightLightBrushKey, colors["App.PanelBrush"]);
        SetSystemBrush(resources, SystemColors.ControlLightBrushKey, colors["App.SubtlePanelBrush"]);
        SetSystemBrush(resources, SystemColors.ControlDarkBrushKey, colors["App.BorderBrush"]);
        SetSystemBrush(resources, SystemColors.ControlDarkDarkBrushKey, colors["App.BorderBrush"]);
        SetSystemBrush(resources, SystemColors.GrayTextBrushKey, colors["App.MutedBrush"]);
        SetSystemBrush(resources, SystemColors.HighlightBrushKey, colors["App.SelectionBrush"]);
        SetSystemBrush(resources, SystemColors.HighlightTextBrushKey, colors["App.SelectionTextBrush"]);
        SetSystemBrush(resources, SystemColors.InactiveSelectionHighlightBrushKey, colors["App.InactiveSelectionBrush"]);
        SetSystemBrush(resources, SystemColors.InactiveSelectionHighlightTextBrushKey, colors["App.SelectionTextBrush"]);
        SetSystemBrush(resources, SystemColors.MenuBrushKey, colors["App.MenuBackgroundBrush"]);
        SetSystemBrush(resources, SystemColors.MenuTextBrushKey, colors["App.TextBrush"]);

        if (Application.Current is not null)
        {
            foreach (Window window in Application.Current.Windows)
            {
                window.InvalidateVisual();
            }
        }

        WindowTitleBarTheme.ApplyToAllWindows(isDark);
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        // Prefer mutating the existing instance so both DynamicResource and any leftover
        // StaticResource references (same Freezable) update immediately.
        if (resources[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static void SetSystemBrush(ResourceDictionary resources, object key, Color color)
    {
        if (resources.Contains(key) && resources[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static readonly Dictionary<string, Color> Light = new(StringComparer.Ordinal)
    {
        ["App.SurfaceBrush"] = Rgb(247, 248, 250),
        ["App.PanelBrush"] = Rgb(255, 255, 255),
        ["App.SubtlePanelBrush"] = Rgb(249, 250, 251),
        ["App.BorderBrush"] = Rgb(216, 222, 230),
        ["App.TextBrush"] = Rgb(31, 41, 55),
        ["App.MutedBrush"] = Rgb(107, 114, 128),
        ["App.AccentBrush"] = Rgb(37, 99, 235),
        ["App.DangerBrush"] = Rgb(220, 38, 38),
        ["App.WarnBrush"] = Rgb(180, 83, 9),
        ["App.ButtonBackgroundBrush"] = Rgb(238, 242, 255),
        ["App.StatusBarBrush"] = Rgb(239, 242, 246),
        ["App.MenuBackgroundBrush"] = Rgb(247, 248, 250),
        ["App.ControlBackgroundBrush"] = Rgb(243, 244, 246),
        ["App.ToolbarHoverBrush"] = Rgb(229, 231, 235),
        ["App.ToolbarCheckedBrush"] = Rgb(209, 213, 219),
        ["App.ToolbarCheckedHoverBrush"] = Rgb(196, 201, 208),
        ["App.InputBackgroundBrush"] = Rgb(255, 255, 255),
        ["App.GridLineBrush"] = Rgb(238, 238, 238),
        ["App.SelectionBrush"] = Color.FromArgb(0x80, 0x25, 0x63, 0xEB),
        ["App.SelectionTextBrush"] = Rgb(31, 41, 55),
        ["App.InactiveSelectionBrush"] = Color.FromArgb(0x40, 0x25, 0x63, 0xEB),
        ["App.DropIntoBrush"] = Color.FromArgb(0x33, 0x25, 0x63, 0xEB),
    };

    private static readonly Dictionary<string, Color> Dark = new(StringComparer.Ordinal)
    {
        ["App.SurfaceBrush"] = Rgb(26, 29, 35),
        ["App.PanelBrush"] = Rgb(34, 38, 46),
        ["App.SubtlePanelBrush"] = Rgb(42, 47, 56),
        ["App.BorderBrush"] = Rgb(58, 64, 76),
        ["App.TextBrush"] = Rgb(229, 231, 235),
        ["App.MutedBrush"] = Rgb(156, 163, 175),
        ["App.AccentBrush"] = Rgb(59, 130, 246),
        ["App.DangerBrush"] = Rgb(248, 113, 113),
        ["App.WarnBrush"] = Rgb(251, 191, 36),
        ["App.ButtonBackgroundBrush"] = Rgb(47, 53, 64),
        ["App.StatusBarBrush"] = Rgb(24, 27, 33),
        ["App.MenuBackgroundBrush"] = Rgb(26, 29, 35),
        ["App.ControlBackgroundBrush"] = Rgb(47, 53, 64),
        ["App.ToolbarHoverBrush"] = Rgb(51, 56, 66),
        ["App.ToolbarCheckedBrush"] = Rgb(63, 70, 84),
        ["App.ToolbarCheckedHoverBrush"] = Rgb(74, 82, 98),
        ["App.InputBackgroundBrush"] = Rgb(30, 34, 42),
        ["App.GridLineBrush"] = Rgb(58, 64, 76),
        ["App.SelectionBrush"] = Color.FromArgb(0x80, 0x3B, 0x82, 0xF6),
        ["App.SelectionTextBrush"] = Rgb(243, 244, 246),
        ["App.InactiveSelectionBrush"] = Color.FromArgb(0x40, 0x3B, 0x82, 0xF6),
        ["App.DropIntoBrush"] = Color.FromArgb(0x33, 0x3B, 0x82, 0xF6),
    };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
