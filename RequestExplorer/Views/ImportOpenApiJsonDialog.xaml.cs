using System.Windows;
using RequestExplorer.Services;

namespace RequestExplorer.Views;

public partial class ImportOpenApiJsonDialog : Window
{
    private readonly string _baseline = string.Empty;
    private bool _applySucceeded;

    public ImportOpenApiJsonDialog()
    {
        InitializeComponent();
        EditorBox.Text = string.Empty;
        _baseline = EditorBox.Text;
        Loaded += (_, _) => EditorBox.Focus();
    }

    /// <summary>
    /// Invoked on Apply. Return true to close; false to keep the dialog open with <paramref name="error"/>.
    /// </summary>
    public Func<string, (bool ok, string? error)>? TryApply { get; set; }

    public string JsonText => EditorBox.Text ?? string.Empty;

    public void SetImportTargetDescription(string description)
    {
        SubtitleText.Text =
            $"Paste an OpenAPI 3.x or Swagger 2.0 JSON document. {description}";
    }

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        if (!SubtreeJsonService.TryFormat(EditorBox.Text, out var formatted, out var error))
        {
            ShowError(error ?? "Could not format JSON.");
            return;
        }

        EditorBox.Text = formatted;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        if (TryApply is null)
        {
            ShowError("Apply handler is not configured.");
            return;
        }

        if (string.IsNullOrWhiteSpace(EditorBox.Text))
        {
            ShowError("Paste an OpenAPI or Swagger JSON document first.");
            return;
        }

        var (ok, error) = TryApply(EditorBox.Text);
        if (!ok)
        {
            ShowError(error ?? "Import failed.");
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
            "Discard paste?",
            "The JSON buffer has unsaved content. Discard it and close?",
            yesText: "Discard",
            noText: "Keep editing");
        if (!discard)
        {
            e.Cancel = true;
        }
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
