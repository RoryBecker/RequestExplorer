using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace RequestExplorer.Views;

internal static class TitleEditHelper
{
    public static void Attach(TextBox titleBox)
    {
        titleBox.KeyDown += OnKeyDown;
    }

    public static void FocusAndSelect(TextBox? titleBox)
    {
        if (titleBox is null)
        {
            return;
        }

        if (titleBox.Focus())
        {
            titleBox.SelectAll();
            return;
        }

        titleBox.Dispatcher.BeginInvoke(() =>
        {
            if (titleBox.Focus())
            {
                titleBox.SelectAll();
            }
        }, DispatcherPriority.Loaded);
    }

    public static void Commit(TextBox titleBox)
    {
        var binding = BindingOperations.GetBindingExpression(titleBox, TextBox.TextProperty);
        binding?.UpdateSource();
    }

    public static void CommitIfFocusedTitle(params TextBox?[] titleBoxes)
    {
        if (Keyboard.FocusedElement is not TextBox focused)
        {
            return;
        }

        foreach (var box in titleBoxes)
        {
            if (ReferenceEquals(box, focused))
            {
                Commit(focused);
                return;
            }
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox titleBox)
        {
            return;
        }

        Commit(titleBox);
        MoveFocusAway(titleBox);
        e.Handled = true;
    }

    private static void MoveFocusAway(TextBox titleBox)
    {
        var request = new TraversalRequest(FocusNavigationDirection.Next);
        if (!titleBox.MoveFocus(request))
        {
            Keyboard.ClearFocus();
        }
    }
}
