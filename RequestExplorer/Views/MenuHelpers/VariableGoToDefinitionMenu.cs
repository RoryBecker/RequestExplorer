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
/// Adds "Go to Definition" for resolved <c>{{name}}</c> tokens and "Define Variable…"
/// for tokens that have no ancestor definition.
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
                textBox.Loaded -= OnTextBoxLoaded;
                textBox.PreviewMouseLeftButtonDown -= OnTextBoxCtrlClick;
                if (e.NewValue is true)
                {
                    textBox.ContextMenuOpening += OnTextBoxMenuOpening;
                    textBox.Loaded += OnTextBoxLoaded;
                    textBox.PreviewMouseLeftButtonDown += OnTextBoxCtrlClick;
                    if (textBox.IsLoaded)
                    {
                        EnsureEditContextMenu(textBox);
                    }
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

    private static void OnTextBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            EnsureEditContextMenu(textBox);
        }
    }

    /// <summary>Ctrl+click a <c>{{name}}</c> token jumps to its definition (or offers Define Variable).</summary>
    private static void OnTextBoxCtrlClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || Keyboard.Modifiers != ModifierKeys.Control
            || sender is not TextBox textBox)
        {
            return;
        }

        var from = FindOrigin(textBox);
        var names = TokenNamesAtPoint(textBox, e);
        if (names.Count == 0)
        {
            return;
        }

        var name = names[0];
        if (!VariableDefinitionLookup.TryFind(from, name, out _, out _))
        {
            return;
        }

        Navigate(from, name);
        e.Handled = true;
    }

    private static void OnTextBoxMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        var menu = EnsureEditContextMenu(textBox);
        var from = FindOrigin(textBox);
        SyncItems(menu, TokenNamesInTextBox(textBox), from);
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
        var created = textBlock.ContextMenu is null;
        var menu = textBlock.ContextMenu ??= new ContextMenu();
        SyncItems(menu, names, from);
        if (!HasVariableMenuItems(menu) && menu.Items.Count == 0)
        {
            if (created)
            {
                textBlock.ContextMenu = null;
            }

            e.Handled = true;
        }
    }

    public static IReadOnlyList<string> TokenNamesInTextBox(TextBox textBox) =>
        VariableResolver.CollectTokenNames(
            textBox.Text,
            GetCharIndex(textBox),
            textBox.SelectionStart,
            textBox.SelectionLength);

    private static IReadOnlyList<string> TokenNamesAtPoint(TextBox textBox, MouseEventArgs e)
    {
        try
        {
            var index = textBox.GetCharacterIndexFromPoint(e.GetPosition(textBox), true);
            return VariableResolver.CollectTokenNames(
                textBox.Text,
                index,
                selectionStart: 0,
                selectionLength: 0);
        }
        catch
        {
            // Hit-testing can fail while the visual is updating.
        }

        return [];
    }

    public static void SyncItems(
        ContextMenu menu,
        IEnumerable<string> names,
        TreeNodeViewModel? from,
        IReadOnlyList<GoToDefinitionTarget>? extraGotoTargets = null)
    {
        ClearItems(menu);

        var gotoTargets = new List<GoToDefinitionTarget>();
        var seenGoto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (extraGotoTargets is not null)
        {
            foreach (var extra in extraGotoTargets)
            {
                if (seenGoto.Add(extra.Name))
                {
                    gotoTargets.Add(extra);
                }
            }
        }

        var undefined = new List<string>();
        var seenUndefined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var canDefine = VariableDefinitionLookup.ResolveOwner(from) is not null;
        foreach (var raw in names)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var name = raw.Trim();
            if (VariableDefinitionLookup.TryFind(from, name, out _, out _))
            {
                if (seenGoto.Add(name))
                {
                    var captured = name;
                    gotoTargets.Add(new GoToDefinitionTarget(captured, () => Navigate(from, captured)));
                }
            }
            else if (canDefine && seenUndefined.Add(name))
            {
                undefined.Add(name);
            }
        }

        if (gotoTargets.Count == 0 && undefined.Count == 0)
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

        insertAt = InsertActionGroup(
            menu,
            insertAt,
            "Go to _Definition",
            gotoTargets.Select(t => (t.Name, t.Navigate)));
        InsertActionGroup(
            menu,
            insertAt,
            "Define _Variable…",
            undefined.Select(n =>
            {
                var captured = n;
                return (captured, (Action)(() => Define(from, captured)));
            }));
    }

    public static bool HasVariableMenuItems(ContextMenu menu) =>
        menu.Items.OfType<MenuItem>().Any(i => Equals(i.Tag, MenuTag));

    public static void Navigate(TreeNodeViewModel? from, string name)
    {
        if (Application.Current?.MainWindow?.DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.GoToVariableDefinition(name, from);
    }

    public static void Define(TreeNodeViewModel? from, string name)
    {
        if (Application.Current?.MainWindow?.DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.DefineVariable(name, from);
    }

    private static int InsertActionGroup(
        ContextMenu menu,
        int insertAt,
        string header,
        IEnumerable<(string Name, Action Navigate)> targets)
    {
        var list = targets.ToList();
        if (list.Count == 0)
        {
            return insertAt;
        }

        if (list.Count == 1)
        {
            var target = list[0];
            var item = new MenuItem
            {
                Header = header,
                Tag = MenuTag
            };
            item.Click += (_, _) => target.Navigate();
            menu.Items.Insert(insertAt, item);
            return insertAt + 1;
        }

        var parent = new MenuItem
        {
            Header = header,
            Tag = MenuTag
        };
        foreach (var target in list)
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
        return insertAt + 1;
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

            if (current is FrameworkElement { DataContext: MainViewModel main })
            {
                return main.SelectedRequest ?? main.SelectedNode;
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
