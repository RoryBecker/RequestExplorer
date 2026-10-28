using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Views.MenuHelpers;

/// <summary>
/// URL field: "Insert" and "Replace with" submenus listing every enabled ancestor variable.
/// Both items stay visible; Insert is enabled with a caret (no selection), Replace with
/// when there is a selection to replace.
/// </summary>
public static class UrlReplaceWithVariableMenu
{
    private const string MenuTag = "url-replace-with-variable";
    private const int TooltipValueMaxChars = 64;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(UrlReplaceWithVariableMenu),
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
        textBox.Loaded -= OnLoaded;

        if (e.NewValue is true)
        {
            textBox.ContextMenuOpening += OnContextMenuOpening;
            textBox.Loaded += OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            EnsureContextMenu(textBox);
        }
    }

    private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        var menu = EnsureContextMenu(textBox);
        ClearItems(menu);

        var request = textBox.DataContext as RequestNodeViewModel;
        var variables = request is null ? [] : CollectAncestorVariables(request);
        var hasVariables = variables.Count > 0;
        var hasSelection = textBox.SelectionLength > 0;

        var insertAt = InsertIndex(menu);
        if (insertAt > 0 && menu.Items[insertAt - 1] is not Separator)
        {
            menu.Items.Insert(insertAt, new Separator { Tag = MenuTag });
            insertAt++;
        }

        menu.Items.Insert(
            insertAt++,
            CreateSubmenu(
                "_Insert",
                variables,
                isEnabled: hasVariables && !hasSelection,
                name => ReplaceMatchingVariableMenu.InsertTokenAtCaret(textBox, name)));

        menu.Items.Insert(
            insertAt,
            CreateSubmenu(
                "_Replace with",
                variables,
                isEnabled: hasVariables && hasSelection,
                name => ReplaceMatchingVariableMenu.ReplaceSelectionWithToken(textBox, name)));
    }

    private static MenuItem CreateSubmenu(
        string header,
        IReadOnlyList<AncestorVariable> variables,
        bool isEnabled,
        Action<string> onPick)
    {
        var parent = new MenuItem
        {
            Header = header,
            Tag = MenuTag,
            IsEnabled = isEnabled
        };

        foreach (var variable in variables)
        {
            var name = variable.Name;
            var item = new MenuItem
            {
                Header = "{{" + EscapeMnemonic(name) + "}}",
                Tag = MenuTag,
                ToolTip = FormatTooltip(variable)
            };
            item.Click += (_, _) => onPick(name);
            parent.Items.Add(item);
        }

        return parent;
    }

    private static List<AncestorVariable> CollectAncestorVariables(TreeNodeViewModel request)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AncestorVariable>();

        for (var node = request.Parent; node is not null; node = node.Parent)
        {
            if (node is not ContainerNodeViewModel container)
            {
                continue;
            }

            foreach (var variable in container.Variables)
            {
                if (!variable.IsEnabled || !variable.HasKey)
                {
                    continue;
                }

                var key = variable.Key.Trim();
                if (!seen.Add(key))
                {
                    continue;
                }

                result.Add(new AncestorVariable(
                    key,
                    container.Name,
                    variable.Value ?? string.Empty,
                    variable.IsSecret));
            }
        }

        return result;
    }

    private static string FormatTooltip(AncestorVariable variable)
    {
        var value = variable.IsSecret
            ? "(secret)"
            : Truncate(variable.Value);
        return $"{variable.Source}: {value}";
    }

    private static string Truncate(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(empty)";
        }

        var compact = value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return compact.Length <= TooltipValueMaxChars
            ? compact
            : compact[..TooltipValueMaxChars] + "…";
    }

    private static int InsertIndex(ContextMenu menu)
    {
        var extract = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(i => Equals(i.Tag, "url-extract-variable"));
        return extract is null ? menu.Items.Count : menu.Items.IndexOf(extract) + 1;
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

    private static string EscapeMnemonic(string value) =>
        value.Replace("_", "__", StringComparison.Ordinal);

    private readonly record struct AncestorVariable(
        string Name,
        string Source,
        string Value,
        bool IsSecret);
}
