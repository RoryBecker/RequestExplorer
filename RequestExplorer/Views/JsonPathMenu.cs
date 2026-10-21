using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RequestExplorer.Services;

namespace RequestExplorer.Views;

/// <summary>
/// Adds "Copy JSON path" / "Copy XML path" to a TextBox context menu when the
/// caret/click is inside valid JSON or XML.
/// </summary>
public static class JsonPathMenu
{
    private const string MenuTag = "json-caret-path";
    private const int HeaderPathMaxChars = 48;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(JsonPathMenu),
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
        ClearPathItems(menu);

        var index = GetJsonIndex(textBox);
        var text = textBox.Text ?? string.Empty;
        string? kind = null;
        string? path = null;
        if (JsonCaretPath.TryGetPath(text, index, out var jsonPath))
        {
            kind = "JSON";
            path = jsonPath;
        }
        else if (XmlCaretPath.TryGetPath(text, index, out var xmlPath))
        {
            kind = "XML";
            path = xmlPath;
        }

        if (kind is null || path is null)
        {
            return;
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator { Tag = MenuTag });
        }

        var captured = path;
        var item = new MenuItem
        {
            Header = FormatHeader(kind, captured),
            Tag = MenuTag,
            ToolTip = captured
        };
        item.Click += (_, _) => CopyPath(captured);
        menu.Items.Add(item);
    }

    private static int GetJsonIndex(TextBox textBox)
    {
        try
        {
            var point = Mouse.GetPosition(textBox);
            var fromPoint = textBox.GetCharacterIndexFromPoint(point, snapToText: true);
            if (fromPoint >= 0)
            {
                return fromPoint;
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

    private static string FormatHeader(string kind, string path)
    {
        var shown = path.Length <= HeaderPathMaxChars
            ? path
            : path[..(HeaderPathMaxChars - 1)] + "…";
        return $"Copy {kind} path ({EscapeMnemonic(shown)})";
    }

    private static string EscapeMnemonic(string value) =>
        value.Replace("_", "__", StringComparison.Ordinal);

    private static void CopyPath(string path)
    {
        try
        {
            Clipboard.SetText(path);
        }
        catch
        {
            // Clipboard can be locked by another process.
        }
    }

    private static ContextMenu EnsureContextMenu(TextBox textBox)
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

    private static void ClearPathItems(ContextMenu menu)
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
}
