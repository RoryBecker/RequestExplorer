using System.Windows;

namespace RequestExplorer.Views;

public enum ConfirmSaveChoice
{
    Cancel,
    Discard,
    Save
}

public partial class ConfirmSaveDialog : Window
{
    public ConfirmSaveDialog()
    {
        InitializeComponent();
    }

    public ConfirmSaveChoice Choice { get; private set; } = ConfirmSaveChoice.Cancel;

    /// <summary>Escape and Cancel both return <see cref="ConfirmSaveChoice.Cancel"/>.</summary>
    public static ConfirmSaveChoice Prompt(Window? owner, string? message = null)
    {
        var dialog = new ConfirmSaveDialog
        {
            Owner = owner ?? Application.Current?.MainWindow
        };

        if (!string.IsNullOrWhiteSpace(message))
        {
            dialog.MessageText.Text = message;
        }

        dialog.ShowDialog();
        return dialog.Choice;
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        Choice = ConfirmSaveChoice.Save;
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
