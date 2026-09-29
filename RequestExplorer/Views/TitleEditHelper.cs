using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Views;

internal static class TitleEditHelper
{
    public static void Attach(TextBox titleBox)
    {
        titleBox.KeyDown += OnKeyDown;
        titleBox.LostFocus += OnLostFocus;
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
        if (titleBox.DataContext is TreeNodeViewModel node)
        {
            var vm = FindMainViewModel(titleBox);
            vm?.CommitTitleRename(node, titleBox.Text ?? string.Empty);
            BindingOperations.GetBindingExpression(titleBox, TextBox.TextProperty)?.UpdateTarget();
            return;
        }

        BindingOperations.GetBindingExpression(titleBox, TextBox.TextProperty)?.UpdateSource();
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

    private static void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox titleBox)
        {
            Commit(titleBox);
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

    private static MainViewModel? FindMainViewModel(DependencyObject from)
    {
        if (Window.GetWindow(from)?.DataContext is MainViewModel vm)
        {
            return vm;
        }

        return null;
    }
}
