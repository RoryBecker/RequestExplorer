using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RequestExplorer.Services;
using RequestExplorer.Services.Undo;
using RequestExplorer.Services.Variables;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Views;

/// <summary>
/// Adds "Extract as Variable" to a TextBox context menu when text is selected
/// (intended for the request URL field).
/// </summary>
public static class UrlExtractAsVariableMenu
{
    private const string ExtractMenuTag = "url-extract-variable";

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(UrlExtractAsVariableMenu),
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
        var extractItem = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(i => Equals(i.Tag, ExtractMenuTag));
        if (extractItem is null)
        {
            return;
        }

        var canExtract = textBox.SelectionLength > 0
                         && textBox.DataContext is RequestNodeViewModel { Parent: ContainerNodeViewModel };
        extractItem.IsEnabled = canExtract;
        extractItem.Visibility = canExtract ? Visibility.Visible : Visibility.Collapsed;

        if (menu.Items.OfType<Separator>().FirstOrDefault(s => Equals(s.Tag, ExtractMenuTag))
            is { } separator)
        {
            separator.Visibility = extractItem.Visibility;
        }
    }

    private static ContextMenu EnsureContextMenu(TextBox textBox)
    {
        if (textBox.ContextMenu is { } existing
            && existing.Items.OfType<MenuItem>().Any(i => Equals(i.Tag, ExtractMenuTag)))
        {
            return existing;
        }

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Cu_t", Command = ApplicationCommands.Cut });
        menu.Items.Add(new MenuItem { Header = "Cop_y", Command = ApplicationCommands.Copy });
        menu.Items.Add(new MenuItem { Header = "_Paste", Command = ApplicationCommands.Paste });
        menu.Items.Add(new Separator { Tag = ExtractMenuTag });

        var extract = new MenuItem
        {
            Header = "Extract as _Variable…",
            Tag = ExtractMenuTag
        };
        extract.Click += (_, _) => OnExtractClick(textBox);
        menu.Items.Add(extract);

        textBox.ContextMenu = menu;
        return menu;
    }

    private static void OnExtractClick(TextBox textBox)
    {
        if (textBox.DataContext is not RequestNodeViewModel request
            || textBox.SelectionLength <= 0)
        {
            return;
        }

        var selected = textBox.SelectedText;
        var start = textBox.SelectionStart;
        var length = textBox.SelectionLength;
        var owner = Window.GetWindow(textBox);
        var parentName = request.Parent?.Name ?? "parent";

        var dialog = new NamePromptDialog(
            "Extract as Variable",
            $"Variable name (will be added on '{parentName}'):",
            VariableExtract.SuggestVariableName(selected))
        {
            Owner = owner
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = VariableExtract.TryExtractSelectedText(
            request,
            selected,
            start,
            length,
            dialog.ResultName,
            GetUndoRecorder(request),
            out var resolvedParentName);

        switch (result)
        {
            case VariableExtractResult.Success:
                PlaceCaretAfterToken(textBox, request, start);
                break;

            case VariableExtractResult.EmptyName:
                MessageBox.Show(
                    owner,
                    "Variable name cannot be empty.",
                    "Extract as Variable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                break;

            case VariableExtractResult.InvalidName:
                MessageBox.Show(
                    owner,
                    "Variable name cannot contain '{{' or '}}'.",
                    "Extract as Variable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                break;

            case VariableExtractResult.DuplicateName:
                MessageBox.Show(
                    owner,
                    $"A variable named '{dialog.ResultName.Trim()}' already exists on '{resolvedParentName}'.\n\n"
                    + "Choose a different name or edit the existing variable.",
                    "Variable already exists",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                break;

            case VariableExtractResult.NoParent:
                MessageBox.Show(
                    owner,
                    "This request has no parent collection or folder to hold variables.",
                    "Extract as Variable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                break;
        }
    }

    private static void PlaceCaretAfterToken(TextBox textBox, RequestNodeViewModel request, int start)
    {
        var address = request.Address ?? string.Empty;
        var tokenLength = FindTokenLengthAt(address, start);
        textBox.CaretIndex = Math.Min(start + tokenLength, address.Length);
        textBox.SelectionLength = 0;
        textBox.Focus();
    }

    private static int FindTokenLengthAt(string address, int start)
    {
        if (start < 0 || start >= address.Length || address[start] != '{')
        {
            return 0;
        }

        var end = address.IndexOf("}}", start, StringComparison.Ordinal);
        return end < 0 ? 0 : end + 2 - start;
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
}
