using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RequestExplorer.Models;
using RequestExplorer.Services;

namespace RequestExplorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CollectionPersistenceService _persistence;
    private readonly HttpRequestExecutor _httpExecutor;
    private CancellationTokenSource? _sendCts;

    public MainViewModel()
        : this(new CollectionPersistenceService(), new HttpRequestExecutor())
    {
    }

    public MainViewModel(CollectionPersistenceService persistence, HttpRequestExecutor httpExecutor)
    {
        _persistence = persistence;
        _httpExecutor = httpExecutor;
        LoadWorkspace();
    }

    public ObservableCollection<CollectionNodeViewModel> Collections { get; } = [];

    public ResponseViewModel Response { get; } = new();

    public string StorageDirectory => _persistence.StorageDirectory;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    [NotifyPropertyChangedFor(nameof(SelectedRequest))]
    [NotifyPropertyChangedFor(nameof(IsRequestSelected))]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddRequestCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendRequestCommand))]
    private TreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendRequestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSendCommand))]
    private bool _isSending;

    public RequestNodeViewModel? SelectedRequest => SelectedNode as RequestNodeViewModel;

    public bool IsRequestSelected => SelectedRequest is not null;

    public string SelectionSummary
    {
        get
        {
            if (SelectedNode is null)
            {
                return "Nothing selected";
            }

            return SelectedNode switch
            {
                CollectionNodeViewModel => $"Collection · {SelectedNode.Name}",
                FolderNodeViewModel => $"Folder · {SelectedNode.Name}",
                RequestNodeViewModel request => $"Request · {request.Verb} · {request.Name}",
                _ => SelectedNode.Name
            };
        }
    }

    public bool HasAnyDirty => Collections.Any(CollectionTreeMapper.HasDirty);

    partial void OnSelectedNodeChanged(TreeNodeViewModel? oldValue, TreeNodeViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    private void LoadWorkspace()
    {
        Collections.Clear();

        try
        {
            var loaded = _persistence.LoadAll();
            if (loaded.Count == 0)
            {
                SeedAndPersistDemo();
                StatusMessage = $"Seeded demo collections → {_persistence.StorageDirectory}";
                return;
            }

            foreach (var definition in loaded)
            {
                Collections.Add(CollectionTreeMapper.ToViewModel(definition));
            }

            StatusMessage = $"Loaded {Collections.Count} collection(s) from {_persistence.StorageDirectory}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not load collections.\n\n{ex.Message}",
                "Load Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            foreach (var node in SampleDataFactory.CreateDemoWorkspace())
            {
                Collections.Add(node);
            }

            StatusMessage = "Load failed — using in-memory demo (not saved)";
        }
    }

    private void SeedAndPersistDemo()
    {
        foreach (var definition in SampleDataFactory.CreateDemoDefinitions())
        {
            _persistence.Save(definition);
            Collections.Add(CollectionTreeMapper.ToViewModel(definition));
        }
    }

    [RelayCommand]
    private void Save()
    {
        var collection = CollectionTreeMapper.FindRootCollection(SelectedNode);
        if (collection is null)
        {
            if (Collections.Count == 0)
            {
                StatusMessage = "Nothing to save";
                return;
            }

            SaveAllCollections();
            return;
        }

        try
        {
            SaveCollection(collection);
            StatusMessage = $"Saved '{collection.Name}'";
        }
        catch (Exception ex)
        {
            StatusMessage = "Save failed";
            MessageBox.Show(
                ex.Message,
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void SaveAll()
    {
        try
        {
            SaveAllCollections();
        }
        catch (Exception ex)
        {
            StatusMessage = "Save failed";
            MessageBox.Show(
                ex.Message,
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveAllCollections()
    {
        foreach (var collection in Collections)
        {
            SaveCollection(collection);
        }

        StatusMessage = $"Saved all ({Collections.Count}) collection(s)";
    }

    private void SaveCollection(CollectionNodeViewModel collection)
    {
        CollectionTreeMapper.SyncModelFromView(collection);
        _persistence.Save(collection.Model);
        CollectionTreeMapper.ClearDirty(collection);
        OnPropertyChanged(nameof(HasAnyDirty));
    }

    [RelayCommand(CanExecute = nameof(CanSendRequest))]
    private async Task SendRequestAsync()
    {
        if (SelectedRequest is null || IsSending)
        {
            return;
        }

        _sendCts?.Cancel();
        _sendCts?.Dispose();
        _sendCts = new CancellationTokenSource();

        IsSending = true;
        Response.BeginSend();
        StatusMessage = $"Sending {SelectedRequest.Verb} {SelectedRequest.Address}…";

        try
        {
            var result = await _httpExecutor.SendAsync(SelectedRequest.Model, _sendCts.Token);
            Response.Apply(result);

            StatusMessage = result.Succeeded
                ? $"Response {result.StatusCode} in {Response.ElapsedDisplay}"
                : $"Send failed: {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            Response.Apply(new HttpExecutionResult
            {
                Succeeded = false,
                ErrorMessage = ex.Message
            });
            StatusMessage = $"Send failed: {ex.Message}";
        }
        finally
        {
            IsSending = false;
            Response.IsSending = false;
            SendRequestCommand.NotifyCanExecuteChanged();
            CancelSendCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSendRequest() => IsRequestSelected && !IsSending;

    [RelayCommand(CanExecute = nameof(CanCancelSend))]
    private void CancelSend()
    {
        _sendCts?.Cancel();
        StatusMessage = "Cancelling…";
    }

    private bool CanCancelSend() => IsSending;

    [RelayCommand]
    private void AddCollection()
    {
        if (!TryPromptName("New Collection", "Collection name:", "New Collection", out var name))
        {
            return;
        }

        var model = new CollectionDefinition { Name = name };
        var node = new CollectionNodeViewModel(model);
        Collections.Add(node);
        SelectedNode = node;
        StatusMessage = $"Created collection '{name}' (Ctrl+S to persist)";
    }

    [RelayCommand(CanExecute = nameof(CanAddFolder))]
    private void AddFolder()
    {
        if (SelectedNode is null || !CanAddFolder())
        {
            return;
        }

        if (!TryPromptName("New Folder", "Folder name:", "New Folder", out var name))
        {
            return;
        }

        var model = new FolderDefinition { Name = name };
        var node = new FolderNodeViewModel(model);

        switch (SelectedNode)
        {
            case CollectionNodeViewModel collection:
                collection.Model.Folders.Add(model);
                collection.AddChild(node);
                break;
            case FolderNodeViewModel folder:
                folder.Model.Folders.Add(model);
                folder.AddChild(node);
                break;
            default:
                return;
        }

        SelectedNode = node;
        StatusMessage = $"Created folder '{name}' (Ctrl+S to persist)";
    }

    private bool CanAddFolder() =>
        SelectedNode is CollectionNodeViewModel or FolderNodeViewModel;

    [RelayCommand(CanExecute = nameof(CanAddRequest))]
    private void AddRequest()
    {
        if (SelectedNode is null || !CanAddRequest())
        {
            return;
        }

        if (!TryPromptName("New Request", "Request name:", "New Request", out var name))
        {
            return;
        }

        var model = new RequestDefinition
        {
            Name = name,
            Verb = HttpVerb.GET,
            Address = "https://httpbin.org/get"
        };
        var node = new RequestNodeViewModel(model);

        switch (SelectedNode)
        {
            case CollectionNodeViewModel collection:
                collection.Model.Requests.Add(model);
                collection.AddChild(node);
                break;
            case FolderNodeViewModel folder:
                folder.Model.Requests.Add(model);
                folder.AddChild(node);
                break;
            default:
                return;
        }

        SelectedNode = node;
        StatusMessage = $"Created request '{name}' (Ctrl+S to persist)";
    }

    private bool CanAddRequest() =>
        SelectedNode is CollectionNodeViewModel or FolderNodeViewModel;

    [RelayCommand(CanExecute = nameof(CanRenameNode))]
    private void RenameNode()
    {
        if (SelectedNode is null)
        {
            return;
        }

        if (!TryPromptName("Rename", "Name:", SelectedNode.Name, out var name))
        {
            return;
        }

        SelectedNode.Name = name;
        OnPropertyChanged(nameof(SelectionSummary));
        StatusMessage = $"Renamed to '{name}' (Ctrl+S to persist)";
    }

    private bool CanRenameNode() => SelectedNode is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteNode))]
    private void DeleteNode()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var name = SelectedNode.Name;
        var result = MessageBox.Show(
            $"Delete '{name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var toSelect = SelectedNode.Parent;
        RemoveNode(SelectedNode);
        SelectedNode = toSelect;
        StatusMessage = $"Deleted '{name}'";
    }

    private bool CanDeleteNode() => SelectedNode is not null;

    private void RemoveNode(TreeNodeViewModel node)
    {
        if (node is CollectionNodeViewModel collection)
        {
            Collections.Remove(collection);
            try
            {
                _persistence.Delete(collection.Model.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Collection removed from workspace, but the file could not be deleted.\n\n{ex.Message}",
                    "Delete Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return;
        }

        var parent = node.Parent;
        if (parent is null)
        {
            return;
        }

        switch (parent)
        {
            case CollectionNodeViewModel parentCollection when node is FolderNodeViewModel folder:
                parentCollection.Model.Folders.Remove(folder.Model);
                break;
            case CollectionNodeViewModel parentCollection when node is RequestNodeViewModel request:
                parentCollection.Model.Requests.Remove(request.Model);
                break;
            case FolderNodeViewModel parentFolder when node is FolderNodeViewModel folder:
                parentFolder.Model.Folders.Remove(folder.Model);
                break;
            case FolderNodeViewModel parentFolder when node is RequestNodeViewModel request:
                parentFolder.Model.Requests.Remove(request.Model);
                break;
        }

        parent.RemoveChild(node);
    }

    public bool ConfirmClose()
    {
        if (!HasAnyDirty)
        {
            return true;
        }

        var result = MessageBox.Show(
            "You have unsaved changes. Save all before closing?",
            "Unsaved Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        switch (result)
        {
            case MessageBoxResult.Cancel:
                return false;
            case MessageBoxResult.No:
                return true;
            case MessageBoxResult.Yes:
                try
                {
                    SaveAllCollections();
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        "Save Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }
            default:
                return false;
        }
    }

    private static bool TryPromptName(string title, string prompt, string initial, out string name)
    {
        var dialog = new Views.NamePromptDialog(title, prompt, initial)
        {
            Owner = Application.Current?.MainWindow
        };

        var accepted = dialog.ShowDialog() == true;
        name = dialog.ResultName.Trim();
        return accepted && !string.IsNullOrWhiteSpace(name);
    }
}
