using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using RequestExplorer.Services.Undo;
using RequestExplorer.Services.Variables;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Views;

/// <summary>
/// When selected text matches a scoped variable's value, offers
/// "Replace with {{name}}" on the TextBox context menu.
/// </summary>
public static class ReplaceMatchingVariableMenu
{
    private const string MenuTag = "replace-matching-variable";

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ReplaceMatchingVariableMenu),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
        {
            return;
        }

        textBox.ContextMenuOpening -= OnContextMenuOpening;

        if (e.NewValue is true)
        {
            textBox.ContextMenuOpening += OnContextMenuOpening;
        }
    }

    private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        var menu = EnsureContextMenu(textBox);
        ClearReplaceItems(menu);

        if (textBox.SelectionLength <= 0)
        {
            return;
        }

        var selected = textBox.SelectedText;
        if (string.IsNullOrEmpty(selected))
        {
            return;
        }

        var matches = FindMatchingVariableNames(textBox, selected);
        if (matches.Count == 0)
        {
            return;
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator { Tag = MenuTag });
        }

        foreach (var name in matches)
        {
            var captured = name;
            var item = new MenuItem
            {
                Header = $"Replace with {{{{{EscapeMnemonic(captured)}}}}}",
                Tag = MenuTag
            };
            item.Click += (_, _) => ReplaceSelection(textBox, captured);
            menu.Items.Add(item);
        }
    }

    private static ContextMenu EnsureContextMenu(TextBox textBox)
    {
        if (textBox.ContextMenu is { } existing)
        {
            return existing;
        }

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Cu_t", Command = ApplicationCommands.Cut });
        menu.Items.Add(new MenuItem { Header = "Cop_y", Command = ApplicationCommands.Copy });
        menu.Items.Add(new MenuItem { Header = "_Paste", Command = ApplicationCommands.Paste });
        textBox.ContextMenu = menu;
        return menu;
    }

    private static void ClearReplaceItems(ContextMenu menu)
    {
        for (var i = menu.Items.Count - 1; i >= 0; i--)
        {
            switch (menu.Items[i])
            {
                case MenuItem { Tag: string tag } when tag == MenuTag:
                case Separator { Tag: string sepTag } when sepTag == MenuTag:
                    menu.Items.RemoveAt(i);
                    break;
            }
        }
    }

    private static List<string> FindMatchingVariableNames(TextBox textBox, string selected)
    {
        var scope = ResolveScope(textBox);
        var names = new List<string>();
        foreach (var pair in scope)
        {
            if (string.Equals(pair.Value, selected, StringComparison.Ordinal))
            {
                names.Add(pair.Key);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static void ReplaceSelection(TextBox textBox, string variableName)
    {
        if (textBox.SelectionLength <= 0)
        {
            return;
        }

        var start = textBox.SelectionStart;
        var length = textBox.SelectionLength;
        var token = "{{" + variableName + "}}";
        var current = textBox.Text ?? string.Empty;
        if (start < 0 || length < 0 || start + length > current.Length)
        {
            return;
        }

        var newText = string.Concat(
            current.AsSpan(0, start),
            token,
            current.AsSpan(start + length));

        if (TryReplaceBoundAddress(textBox, newText))
        {
            textBox.CaretIndex = Math.Min(start + token.Length, newText.Length);
            textBox.SelectionLength = 0;
            textBox.Focus();
            return;
        }

        textBox.Text = newText;
        textBox.CaretIndex = Math.Min(start + token.Length, newText.Length);
        textBox.SelectionLength = 0;
        BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.UpdateSource();
        textBox.Focus();
    }

    private static bool TryReplaceBoundAddress(TextBox textBox, string newAddress)
    {
        if (textBox.DataContext is not RequestNodeViewModel request)
        {
            return false;
        }

        var binding = BindingOperations.GetBinding(textBox, TextBox.TextProperty);
        var path = binding?.Path?.Path;
        if (!string.Equals(path, nameof(RequestNodeViewModel.Address), StringComparison.Ordinal))
        {
            return false;
        }

        var previous = request.Address ?? string.Empty;
        if (string.Equals(previous, newAddress, StringComparison.Ordinal))
        {
            return true;
        }

        request.ApplyAddress(newAddress);

        if (GetUndoRecorder(request) is { IsApplyingHistory: false } recorder)
        {
            recorder.Record(new SetRequestAddressAction(
                request,
                previous,
                $"Replace with variable in address"));
        }

        BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.UpdateTarget();
        return true;
    }

    private static IUndoRecorder? GetUndoRecorder(RequestNodeViewModel request)
    {
        if (Application.Current?.MainWindow?.DataContext is MainViewModel main)
        {
            return main;
        }

        for (var node = request.Parent; node is not null; node = node.Parent)
        {
            if (node is ContainerNodeViewModel { UndoRecorder: { } recorder })
            {
                return recorder;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> ResolveScope(FrameworkElement element)
    {
        var request = FindAncestorDataContext<RequestNodeViewModel>(element);
        if (request is not null)
        {
            return VariableResolver.BuildScope(request);
        }

        var container = FindAncestorDataContext<ContainerNodeViewModel>(element);
        if (container is not null)
        {
            return VariableResolver.BuildScopeFrom(container);
        }

        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: RequestNodeViewModel req })
            {
                return VariableResolver.BuildScope(req);
            }

            if (current is FrameworkElement { DataContext: ContainerNodeViewModel c })
            {
                return VariableResolver.BuildScopeFrom(c);
            }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static T? FindAncestorDataContext<T>(DependencyObject? start)
        where T : class
    {
        for (DependencyObject? current = start; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: T match })
            {
                return match;
            }
        }

        return null;
    }

    private static string EscapeMnemonic(string value) =>
        value.Replace("_", "__", StringComparison.Ordinal);
}
