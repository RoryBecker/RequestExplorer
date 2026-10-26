using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RequestExplorer.Services.Variables;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Views.MenuHelpers;

/// <summary>
/// Adds "Go to Definition" to a TextBox/TextBlock context menu when the caret/click
/// is on a <c>{{name}}</c> token defined on an ancestor collection or folder.
/// </summary>
public static class VariableGoToDefinitionMenu
{
    public const string MenuTag = "var-goto-definition";

    public sealed record GoToDefinitionTarget(string Name, Action Navigate);

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(VariableGoToDefinitionMenu),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        switch (d)
        {
            case TextBox textBox:
                textBox.ContextMenuOpening -= OnTextBoxMenuOpening;
                if (e.NewValue is true)
                {
                    textBox.ContextMenuOpening += OnTextBoxMenuOpening;
                }

                break;
            case TextBlock textBlock:
                textBlock.ContextMenuOpening -= OnTextBlockMenuOpening;
                if (e.NewValue is true)
                {
                    textBlock.ContextMenuOpening += OnTextBlockMenuOpening;
                }

                break;
        }
    }

    private static void OnTextBoxMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        var menu = EnsureEditContextMenu(textBox);
        var from = FindOrigin(textBox);
        var names = TokenNamesInTextBox(textBox);
        SyncItems(menu, TargetsFromTokens(names, from));
    }

    private static void OnTextBlockMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not TextBlock textBlock)
        {
            return;
        }

        var from = FindOrigin(textBlock);
        var names = from is RequestNodeViewModel request
            ? VariableResolver.FindTokenNames(request.Address)
            : [];
        var targets = TargetsFromTokens(names, from);
        if (targets.Count == 0)
        {
            if (textBlock.ContextMenu is { } existing)
            {
                ClearItems(existing);
                if (existing.Items.Count == 0)
                {
                    e.Handled = true;
                }
            }

            return;
        }

        var menu = textBlock.ContextMenu ??= new ContextMenu();
        SyncItems(menu, targets);
    }

    public static IReadOnlyList<string> TokenNamesInTextBox(TextBox textBox) =>
        VariableResolver.CollectTokenNames(
            textBox.Text,
            GetCharIndex(textBox),
            textBox.SelectionStart,
            textBox.SelectionLength);

    public static IReadOnlyList<GoToDefinitionTarget> TargetsFromTokens(
        IEnumerable<string> names,
        TreeNodeViewModel? from)
    {
        var targets = new List<GoToDefinitionTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in names)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var name = raw.Trim();
            if (!seen.Add(name) || !VariableDefinitionLookup.TryFind(from, name, out _, out _))
            {
                continue;
            }

            var captured = name;
            targets.Add(new GoToDefinitionTarget(captured, () => Navigate(from, captured)));
        }

        return targets;
    }

    public static void SyncItems(ContextMenu menu, IReadOnlyList<GoToDefinitionTarget> targets)
    {
        ClearItems(menu);
        if (targets.Count == 0)
        {
            return;
        }

        var insertAt = IndexAfterCopyValue(menu);
        if (insertAt > 0 && menu.Items[insertAt - 1] is not Separator)
        {
            menu.Items.Insert(insertAt, new Separator { Tag = MenuTag });
            insertAt++;
        }
        else if (insertAt == 0 && menu.Items.Count > 0)
        {
            menu.Items.Insert(0, new Separator { Tag = MenuTag });
            insertAt = 1;
        }

        if (targets.Count == 1)
        {
            var target = targets[0];
            var item = new MenuItem
            {
                Header = "Go to _Definition",
                Tag = MenuTag
            };
            item.Click += (_, _) => target.Navigate();
            menu.Items.Insert(insertAt, item);
            return;
        }

        var parent = new MenuItem
        {
            Header = "Go to _Definition",
            Tag = MenuTag
        };
        foreach (var target in targets)
        {
            var captured = target;
            var child = new MenuItem
            {
                Header = EscapeMnemonic(captured.Name),
                Tag = MenuTag
            };
            child.Click += (_, _) => captured.Navigate();
            parent.Items.Add(child);
        }

        menu.Items.Insert(insertAt, parent);
    }

    public static void Navigate(TreeNodeViewModel? from, string name)
    {
        if (Application.Current?.MainWindow?.DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.GoToVariableDefinition(name, from);
    }

    public static TreeNodeViewModel? FindOrigin(DependencyObject? start)
    {
        for (var current = start; current is not null; current = GetParentSafe(current))
        {
            if (current is FrameworkElement { DataContext: RequestNodeViewModel request })
            {
                return request;
            }

            if (current is FrameworkElement { DataContext: ContainerNodeViewModel container })
            {
                return container;
            }
        }

        return null;
    }

    private static ContextMenu EnsureEditContextMenu(TextBox textBox)
    {
        if (textBox.ContextMenu is { } existing)
        {
            return existing;
        }

        var menu = new ContextMenu();
        if (!textBox.IsReadOnly)
        {
            menu.Items.Add(new MenuItem { Header = "Cu_t", Command = ApplicationCommands.Cut });
        }

        menu.Items.Add(new MenuItem { Header = "Cop_y", Command = ApplicationCommands.Copy });
        if (!textBox.IsReadOnly)
        {
            menu.Items.Add(new MenuItem { Header = "_Paste", Command = ApplicationCommands.Paste });
        }

        textBox.ContextMenu = menu;
        return menu;
    }

    private static void ClearItems(ContextMenu menu)
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

    private static int IndexAfterCopyValue(ContextMenu menu)
    {
        for (var i = 0; i < menu.Items.Count; i++)
        {
            if (menu.Items[i] is MenuItem { Tag: "kv-copy-value" })
            {
                return i + 1;
            }
        }

        return menu.Items.Count;
    }

    private static int GetCharIndex(TextBox textBox)
    {
        try
        {
            var point = Mouse.GetPosition(textBox);
            var index = textBox.GetCharacterIndexFromPoint(point, true);
            if (index >= 0)
            {
                return index;
            }
        }
        catch
        {
            // Hit-testing can fail while the visual is updating.
        }

        if (textBox.SelectionLength > 0)
        {
            return textBox.SelectionStart;
        }

        return textBox.CaretIndex;
    }

    private static string EscapeMnemonic(string value) =>
        value.Replace("_", "__", StringComparison.Ordinal);

    private static DependencyObject? GetParentSafe(DependencyObject? current)
    {
        if (current is null)
        {
            return null;
        }

        if (current is Run run)
        {
            return run.Parent ?? LogicalTreeHelper.GetParent(run);
        }

        try
        {
            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                return VisualTreeHelper.GetParent(current);
            }
        }
        catch (InvalidOperationException)
        {
            // Fall through to logical parent.
        }

        return current switch
        {
            FrameworkContentElement content => content.Parent,
            FrameworkElement fe => fe.Parent,
            _ => LogicalTreeHelper.GetParent(current)
        };
    }
}
