namespace RequestExplorer.Views;

public partial class RequestEditorView
{
    public RequestEditorView()
    {
        InitializeComponent();
        TitleEditHelper.Attach(TitleBox);
    }

    public System.Windows.Controls.TextBox NameTitleBox => TitleBox;
}
