using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace RequestExplorer.Views.OtherHelpers;

/// <summary>
/// Commits in-progress editor state (titles, DataGrid cells, focused TextBox bindings)
/// before Save / Save All keybindings run.
/// </summary>
internal static class EditorCommitHelper
{
    public static void CommitPendingEdits(params TextBox?[] titleBoxes)
    {
        TitleEditHelper.CommitIfFocusedTitle(titleBoxes);

        if (Keyboard.FocusedElement is not DependencyObject focused)
        {
            return;
        }

        var grid = FindAncestor<DataGrid>(focused);
        if (grid is not null)
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        }

        if (focused is TextBox textBox)
        {
            BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.UpdateSource();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current)
                      ?? (current as FrameworkElement)?.Parent as DependencyObject;
        }

        return null;
    }
}
