using System.Windows;

namespace RequestExplorer.Views;

public partial class NamePromptDialog : Window
{
    public NamePromptDialog(string title, string prompt, string initialValue)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        NameBox.Text = initialValue;
        NameBox.SelectAll();
        Loaded += (_, _) => NameBox.Focus();
    }

    public string ResultName { get; private set; } = string.Empty;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultName = NameBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
