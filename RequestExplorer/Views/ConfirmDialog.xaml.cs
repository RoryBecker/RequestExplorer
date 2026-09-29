using System.Windows;

namespace RequestExplorer.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    public bool Confirmed { get; private set; }

    /// <summary>Shows a Yes/No prompt. Escape and No both return false.</summary>
    public static bool Confirm(Window? owner, string title, string message)
    {
        var dialog = new ConfirmDialog(title, message)
        {
            Owner = owner ?? Application.Current?.MainWindow
        };
        dialog.ShowDialog();
        return dialog.Confirmed;
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
    }
}
