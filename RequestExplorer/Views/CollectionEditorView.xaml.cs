namespace RequestExplorer.Views;

public partial class CollectionEditorView
{
    public CollectionEditorView()
    {
        InitializeComponent();
        TitleEditHelper.Attach(TitleBox);
    }

    public System.Windows.Controls.TextBox NameTitleBox => TitleBox;
}
