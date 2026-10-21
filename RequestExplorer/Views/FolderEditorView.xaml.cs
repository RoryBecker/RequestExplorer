using RequestExplorer.Views.OtherHelpers;

namespace RequestExplorer.Views;

public partial class FolderEditorView
{
    public FolderEditorView()
    {
        InitializeComponent();
        TitleEditHelper.Attach(TitleBox);
    }

    public System.Windows.Controls.TextBox NameTitleBox => TitleBox;
}
