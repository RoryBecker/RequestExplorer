using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RequestExplorer.Views;

public enum NewItemKind
{
    Collection,
    Folder,
    Request
}

public partial class NewItemDialog : Window
{
    private readonly RadioButton[] _radios;

    public NewItemDialog(NewItemKind initial, bool canCreateFolder, bool canCreateRequest)
    {
        InitializeComponent();

        FolderRadio.IsEnabled = canCreateFolder;
        RequestRadio.IsEnabled = canCreateRequest;

        _radios = [CollectionRadio, FolderRadio, RequestRadio];
        SelectedKind = ResolveInitial(initial, canCreateFolder, canCreateRequest);
        ApplySelection(SelectedKind);

        Loaded += (_, _) => FocusSelectedRadio();
    }

    public NewItemKind SelectedKind { get; private set; }

    public static bool TryPick(
        Window? owner,
        NewItemKind initial,
        bool canCreateFolder,
        bool canCreateRequest,
        out NewItemKind kind)
    {
        var dialog = new NewItemDialog(initial, canCreateFolder, canCreateRequest)
        {
            Owner = owner ?? Application.Current?.MainWindow
        };

        var accepted = dialog.ShowDialog() == true;
        kind = dialog.SelectedKind;
        return accepted;
    }

    private static NewItemKind ResolveInitial(
        NewItemKind preferred,
        bool canCreateFolder,
        bool canCreateRequest)
    {
        if (IsAvailable(preferred, canCreateFolder, canCreateRequest))
        {
            return preferred;
        }

        foreach (var kind in new[] { NewItemKind.Collection, NewItemKind.Folder, NewItemKind.Request })
        {
            if (IsAvailable(kind, canCreateFolder, canCreateRequest))
            {
                return kind;
            }
        }

        return NewItemKind.Collection;
    }

    private static bool IsAvailable(NewItemKind kind, bool canCreateFolder, bool canCreateRequest) =>
        kind switch
        {
            NewItemKind.Folder => canCreateFolder,
            NewItemKind.Request => canCreateRequest,
            _ => true
        };

    private void ApplySelection(NewItemKind kind)
    {
        SelectedKind = kind;
        CollectionRadio.IsChecked = kind == NewItemKind.Collection;
        FolderRadio.IsChecked = kind == NewItemKind.Folder;
        RequestRadio.IsChecked = kind == NewItemKind.Request;
    }

    private void FocusSelectedRadio()
    {
        var radio = RadioFor(SelectedKind);
        if (radio.IsEnabled)
        {
            radio.Focus();
        }
        else
        {
            CollectionRadio.Focus();
        }
    }

    private RadioButton RadioFor(NewItemKind kind) =>
        kind switch
        {
            NewItemKind.Folder => FolderRadio,
            NewItemKind.Request => RequestRadio,
            _ => CollectionRadio
        };

    private void Radio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true } radio)
        {
            return;
        }

        if (ReferenceEquals(radio, FolderRadio))
        {
            SelectedKind = NewItemKind.Folder;
        }
        else if (ReferenceEquals(radio, RequestRadio))
        {
            SelectedKind = NewItemKind.Request;
        }
        else
        {
            SelectedKind = NewItemKind.Collection;
        }
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
        {
            MoveSelection(e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Return)
        {
            Accept();
            e.Handled = true;
        }
    }

    private void MoveSelection(int delta)
    {
        var index = Array.FindIndex(_radios, r => r.IsChecked == true);
        if (index < 0)
        {
            index = 0;
        }

        for (var step = 0; step < _radios.Length; step++)
        {
            index = (index + delta + _radios.Length) % _radios.Length;
            if (_radios[index].IsEnabled)
            {
                _radios[index].IsChecked = true;
                _radios[index].Focus();
                return;
            }
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Accept()
    {
        if (RadioFor(SelectedKind) is { IsEnabled: false })
        {
            return;
        }

        DialogResult = true;
    }
}
