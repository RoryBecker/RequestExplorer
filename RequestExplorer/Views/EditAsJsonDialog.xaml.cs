using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RequestExplorer.Services.Tree;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Views;

public partial class EditAsJsonDialog : Window
{
    private readonly string _baseline;
    private readonly string _nodeLabel;
    private bool _applySucceeded;
    private int _findFromIndex;

    public EditAsJsonDialog(TreeNodeKind kind, string nodeName, string json)
    {
        InitializeComponent();
        _nodeLabel = string.IsNullOrWhiteSpace(nodeName) ? kind.ToString() : nodeName.Trim();
        Title = $"Edit as JSON — {_nodeLabel}";
        SubtitleText.Text = $"{kind} · internal RequestExplorer JSON (camelCase)";
        EditorBox.Text = json ?? string.Empty;
        _baseline = EditorBox.Text;
        UpdateFindStatus();
    }

    /// <summary>
    /// Invoked after Apply confirmation. Return true to close; false to keep the dialog open with <paramref name="error"/>.
    /// </summary>
    public Func<string, (bool ok, string? error)>? TryApply { get; set; }

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        if (!SubtreeJsonService.TryFormat(EditorBox.Text, out var formatted, out var error))
        {
            ShowError(error ?? "Could not format JSON.");
            return;
        }

        EditorBox.Text = formatted;
        _findFromIndex = 0;
        UpdateFindStatus();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        if (TryApply is null)
        {
            ShowError("Apply handler is not configured.");
            return;
        }

        var confirmed = ConfirmDialog.Confirm(
            this,
            "Confirm Apply",
            $"Replace '{_nodeLabel}' with this JSON?\n\nThis cannot be undone except via Edit → Undo.",
            yesText: "Apply",
            noText: "Cancel");
        if (!confirmed)
        {
            return;
        }

        var (ok, error) = TryApply(EditorBox.Text);
        if (!ok)
        {
            ShowError(error ?? "Apply failed.");
            return;
        }

        _applySucceeded = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_applySucceeded || DialogResult == true)
        {
            return;
        }

        if (string.Equals(EditorBox.Text, _baseline, StringComparison.Ordinal))
        {
            return;
        }

        var discard = ConfirmDialog.Confirm(
            this,
            "Discard edits?",
            "The JSON buffer has unsaved edits. Discard them and close?",
            yesText: "Discard",
            noText: "Keep editing");
        if (!discard)
        {
            e.Cancel = true;
        }
    }

    private void FindBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _findFromIndex = 0;
        UpdateFindStatus();
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => MoveFind(1);

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => MoveFind(-1);

    private void MoveFind(int direction)
    {
        var query = FindBox.Text ?? string.Empty;
        var text = EditorBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(query) || text.Length == 0)
        {
            UpdateFindStatus();
            return;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        int found;
        if (direction >= 0)
        {
            var start = Math.Clamp(_findFromIndex, 0, text.Length);
            found = text.IndexOf(query, start, comparison);
            if (found < 0 && start > 0)
            {
                found = text.IndexOf(query, 0, comparison);
            }
        }
        else
        {
            var start = Math.Clamp(_findFromIndex - 1, 0, Math.Max(0, text.Length - 1));
            found = start >= 0 && text.Length > 0
                ? text.LastIndexOf(query, start, comparison)
                : -1;
            if (found < 0 && _findFromIndex > 0)
            {
                found = text.LastIndexOf(query, text.Length - 1, comparison);
            }
        }

        if (found < 0)
        {
            FindStatusText.Text = "No matches";
            return;
        }

        TextFindHighlight.Apply(EditorBox, found, query.Length);
        _findFromIndex = direction >= 0 ? found + query.Length : found;
        UpdateFindStatus(found);
    }

    private void FindBox_OnPreviewKeyDown(object sender, KeyEventArgs e) =>
        TextFindHighlight.TryHandleFindKeys(e, () => MoveFind(1), () => MoveFind(-1));

    private void UpdateFindStatus(int? currentIndex = null)
    {
        var query = FindBox.Text ?? string.Empty;
        var text = EditorBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            FindStatusText.Text = string.Empty;
            return;
        }

        var count = 0;
        var index = 0;
        var matchOrdinal = 0;
        while (index < text.Length)
        {
            var found = text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                break;
            }

            count++;
            if (currentIndex is int cur && found == cur)
            {
                matchOrdinal = count;
            }

            index = found + Math.Max(1, query.Length);
        }

        FindStatusText.Text = count == 0
            ? "No matches"
            : matchOrdinal > 0
                ? $"{matchOrdinal} / {count}"
                : $"{count} match{(count == 1 ? string.Empty : "es")}";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
