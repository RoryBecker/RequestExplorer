using System.Windows;
using System.Windows.Controls;
using RequestExplorer.Views.OtherHelpers;

namespace RequestExplorer.Views;

public partial class RequestEditorView
{
    public RequestEditorView()
    {
        InitializeComponent();
        TitleEditHelper.Attach(TitleBox);
        DataObject.AddPastingHandler(BearerTokenBox, OnBearerTokenPasting);
    }

    public TextBox NameTitleBox => TitleBox;

    private static void OnBearerTokenPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox box
            || !e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
        {
            return;
        }

        var pasted = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
        if (pasted is null || !TryStripBearerPrefix(pasted, out var stripped))
        {
            return;
        }

        e.CancelCommand();

        var text = box.Text ?? string.Empty;
        var start = box.SelectionStart;
        var length = box.SelectionLength;
        if (start < 0)
        {
            start = 0;
        }

        if (length < 0 || start + length > text.Length)
        {
            length = Math.Max(0, text.Length - start);
        }

        box.Text = text.Remove(start, length).Insert(start, stripped);
        box.CaretIndex = start + stripped.Length;
    }

    private static bool TryStripBearerPrefix(string value, out string stripped)
    {
        const string prefix = "Bearer ";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            stripped = value[prefix.Length..];
            return true;
        }

        stripped = value;
        return false;
    }
}
