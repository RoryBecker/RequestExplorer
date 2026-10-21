using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RequestExplorer.Views.OtherHelpers;

/// <summary>Global Escape handling for open context menus.</summary>
internal static class EscapeCancel
{
    public static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(ContextMenu),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnContextMenuPreviewKeyDown));
    }

    private static void OnContextMenuPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not ContextMenu menu)
        {
            return;
        }

        menu.IsOpen = false;
        e.Handled = true;
    }

    /// <summary>Closes any open <see cref="ContextMenu"/> found under <paramref name="root"/> or focused.</summary>
    public static bool CloseAnyOpenContextMenu(DependencyObject? root)
    {
        if (Keyboard.FocusedElement is DependencyObject focused
            && FindAncestorContextMenu(focused) is { IsOpen: true } focusedMenu)
        {
            focusedMenu.IsOpen = false;
            return true;
        }

        if (root is not null && CloseOpenMenusInSubtree(root))
        {
            return true;
        }

        return false;
    }

    private static ContextMenu? FindAncestorContextMenu(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is ContextMenu menu)
            {
                return menu;
            }

            current = LogicalTreeHelper.GetParent(current)
                      ?? VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool CloseOpenMenusInSubtree(DependencyObject root)
    {
        var closed = false;
        if (root is FrameworkElement { ContextMenu.IsOpen: true } fe)
        {
            fe.ContextMenu.IsOpen = false;
            closed = true;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            closed |= CloseOpenMenusInSubtree(VisualTreeHelper.GetChild(root, i));
        }

        return closed;
    }
}
