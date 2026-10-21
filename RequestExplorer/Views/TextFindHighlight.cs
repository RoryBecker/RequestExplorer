using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace RequestExplorer.Views;

/// <summary>
/// Shared Find-in-text behavior: type the whole query first; Enter / next / previous
/// selects the match. WPF only paints TextBox selection when the target is focused.
/// </summary>
internal static class TextFindHighlight
{
    public static void Apply(TextBox? box, int start, int length)
    {
        if (box is null)
        {
            return;
        }

        var textLength = box.Text?.Length ?? 0;
        if (length <= 0 || start < 0 || start + length > textLength)
        {
            box.Select(0, 0);
            return;
        }

        box.Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.Select(start, length);
            var line = box.GetLineIndexFromCharacterIndex(start);
            if (line >= 0)
            {
                box.ScrollToLine(line);
            }
        }, DispatcherPriority.Input);
    }

    public static bool TryHandleFindKeys(KeyEventArgs e, Action findNext, Action findPrevious)
    {
        if (e.Key is not Key.Enter and not Key.Return)
        {
            return false;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            findPrevious();
        }
        else
        {
            findNext();
        }

        e.Handled = true;
        return true;
    }
}
