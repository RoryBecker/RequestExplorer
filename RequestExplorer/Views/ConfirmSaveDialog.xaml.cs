using System.Windows;

namespace RequestExplorer.Views;

public enum ConfirmSaveChoice
{
    Cancel,
    Discard,
    Save,
    DirtySave
}

public partial class ConfirmSaveDialog : Window
{
    public ConfirmSaveDialog()
    {
        InitializeComponent();
    }

    public ConfirmSaveChoice Choice { get; private set; } = ConfirmSaveChoice.Cancel;

    /// <summary>Escape and Cancel both return <see cref="ConfirmSaveChoice.Cancel"/>.</summary>
    public static ConfirmSaveChoice Prompt(
        Window? owner,
        string? message = null,
        bool allowDirtySave = false)
    {
        var dialog = new ConfirmSaveDialog
        {
            Owner = owner ?? Application.Current?.MainWindow
        };

        if (!string.IsNullOrWhiteSpace(message))
        {
            dialog.MessageText.Text = message;
        }

        dialog.DirtySaveButton.Visibility = allowDirtySave
            ? Visibility.Visible
            : Visibility.Collapsed;

        dialog.ShowDialog();
        return dialog.Choice;
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        Choice = ConfirmSaveChoice.Save;
        DialogResult = true;
    }

    private void DirtySave_Click(object sender, RoutedEventArgs e)
    {
        Choice = ConfirmSaveChoice.DirtySave;
        DialogResult = true;
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        Choice = ConfirmSaveChoice.Discard;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = ConfirmSaveChoice.Cancel;
        DialogResult = false;
    }
}
