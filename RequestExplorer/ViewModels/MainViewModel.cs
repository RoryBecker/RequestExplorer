using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RequestExplorer.Models;
using RequestExplorer.Services;
using RequestExplorer.Services.Postman;
using RequestExplorer.Services.Undo;

namespace RequestExplorer.ViewModels;

public partial class MainViewModel : ObservableObject, IUndoContext, IUndoRecorder
{
    private readonly CollectionPersistenceService _persistence;
    private readonly HttpRequestExecutor _httpExecutor;
    private readonly UndoStack _history = new();
    private CancellationTokenSource? _sendCts;
    private bool _isApplyingHistory;

    public MainViewModel()
        : this(new CollectionPersistenceService(), new HttpRequestExecutor())
    {
    }

    public MainViewModel(CollectionPersistenceService persistence, HttpRequestExecutor httpExecutor)
    {
        _persistence = persistence;
        _httpExecutor = httpExecutor;
        Collections.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCollections));
        _history.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UndoStack.CanUndo) or nameof(UndoStack.UndoDescription))
            {
                UndoCommand.NotifyCanExecuteChanged();
                UndoToCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(UndoMenuHeader));
                OnPropertyChanged(nameof(HasUndoHistory));
                RefreshHistoryMenus();
            }

            if (e.PropertyName is nameof(UndoStack.CanRedo) or nameof(UndoStack.RedoDescription))
            {
                RedoCommand.NotifyCanExecuteChanged();
                RedoToCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(RedoMenuHeader));
                OnPropertyChanged(nameof(HasRedoHistory));
                RefreshHistoryMenus();
            }
        };
        LoadWorkspace();
    }

    public ObservableCollection<CollectionNodeViewModel> Collections { get; } = [];

    public ObservableCollection<HistoryMenuItem> UndoMenuItems { get; } = [];

    public ObservableCollection<HistoryMenuItem> RedoMenuItems { get; } = [];

    public ResponseViewModel Response { get; } = new();

    public string StorageDirectory => _persistence.StorageDirectory;

    public bool HasCollections => Collections.Count > 0;

    public string AppTitle => "RequestExplorer";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    [NotifyPropertyChangedFor(nameof(SelectedRequest))]
    [NotifyPropertyChangedFor(nameof(SelectedCollection))]
    [NotifyPropertyChangedFor(nameof(SelectedFolder))]
    [NotifyPropertyChangedFor(nameof(IsRequestSelected))]
    [NotifyPropertyChangedFor(nameof(IsCollectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsFolderSelected))]
    [NotifyPropertyChangedFor(nameof(IsNodeSelected))]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddRequestCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendRequestCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateRequestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(PasteIntoNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(PasteAtRootCommand))]
    private TreeNodeViewModel? _selectedNode;

    private TreeClipboardPayload? _treeClipboard;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendRequestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSendCommand))]
    private bool _isSending;

    /// <summary>0=Params, 1=Body, 2=Headers, 3=Auth</summary>
    [ObservableProperty]
    private int _selectedRequestTabIndex;

    public RequestNodeViewModel? SelectedRequest => SelectedNode as RequestNodeViewModel;

    public CollectionNodeViewModel? SelectedCollection => SelectedNode as CollectionNodeViewModel;

    public FolderNodeViewModel? SelectedFolder => SelectedNode as FolderNodeViewModel;

    public bool IsRequestSelected => SelectedRequest is not null;

    public bool IsCollectionSelected => SelectedCollection is not null;

    public bool IsFolderSelected => SelectedFolder is not null;

    public bool IsNodeSelected => SelectedNode is not null;

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

    public bool HasTreeClipboard => _treeClipboard is not null;

    public string UndoMenuHeader =>
        _history.CanUndo ? $"_Undo {_history.UndoDescription}" : "_Undo";

    public string RedoMenuHeader =>
        _history.CanRedo ? $"_Redo {_history.RedoDescription}" : "_Redo";

    public bool HasUndoHistory => _history.CanUndo;

    public bool HasRedoHistory => _history.CanRedo;

    partial void OnSelectedNodeChanged(TreeNodeViewModel? oldValue, TreeNodeViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
            oldValue.PropertyChanged -= OnSelectedNodePropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
            newValue.PropertyChanged += OnSelectedNodePropertyChanged;
        }

        CopyNodeCommand.NotifyCanExecuteChanged();
        PasteIntoNodeCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectedNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TreeNodeViewModel.Name) or nameof(RequestNodeViewModel.Verb))
        {
            OnPropertyChanged(nameof(SelectionSummary));
        }

        if (e.PropertyName is nameof(TreeNodeViewModel.IsDirty) or nameof(TreeNodeViewModel.DisplayName))
        {
            OnPropertyChanged(nameof(HasAnyDirty));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => ApplyUndo(forceApply: false);

    private bool CanUndo() =>
        _history.CanUndo && Keyboard.FocusedElement is not TextBox;

    [RelayCommand(CanExecute = nameof(CanUndoTo))]
    private void UndoTo(int depth)
    {
        if (depth <= 0 || !CanUndoTo())
        {
            return;
        }

        var applied = 0;
        for (var i = 0; i < depth; i++)
        {
            if (!_history.CanUndo)
            {
                break;
            }

            if (!ApplyUndo(forceApply: true))
            {
                break;
            }

            applied++;
        }

        if (applied > 0)
        {
            StatusMessage = applied == 1
                ? "Undid 1 action"
                : $"Undid {applied} actions";
        }
    }

    private bool CanUndoTo() => _history.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => ApplyRedo(forceApply: false);

    private bool CanRedo() =>
        _history.CanRedo && Keyboard.FocusedElement is not TextBox;

    [RelayCommand(CanExecute = nameof(CanRedoTo))]
    private void RedoTo(int depth)
    {
        if (depth <= 0 || !CanRedoTo())
        {
            return;
        }

        var applied = 0;
        for (var i = 0; i < depth; i++)
        {
            if (!_history.CanRedo)
            {
                break;
            }

            if (!ApplyRedo(forceApply: true))
            {
                break;
            }

            applied++;
        }

        if (applied > 0)
        {
            StatusMessage = applied == 1
                ? "Redid 1 action"
                : $"Redid {applied} actions";
        }
    }

    private bool CanRedoTo() => _history.CanRedo;

    /// <returns>True when a stack entry was consumed.</returns>
    private bool ApplyUndo(bool forceApply)
    {
        if (!_history.TryPeekUndo(out var action))
        {
            return false;
        }

        if (action.RevealTarget is { } target && !IsRevealSatisfied(target))
        {
            Reveal(target);
            if (!forceApply)
            {
                StatusMessage = $"Showing {DescribeSurface(target.Surface)} — press Ctrl+Z again to undo";
                return false;
            }
        }

        _isApplyingHistory = true;
        try
        {
            var description = _history.UndoDescription;
            _history.Undo(this);
            if (action.RevealTarget is { } after)
            {
                Reveal(after);
            }

            if (!forceApply)
            {
                StatusMessage = $"Undid {description}";
            }

            return true;
        }
        finally
        {
            _isApplyingHistory = false;
        }
    }

    /// <returns>True when a stack entry was consumed.</returns>
    private bool ApplyRedo(bool forceApply)
    {
        if (!_history.TryPeekRedo(out var action))
        {
            return false;
        }

        if (action.RevealTarget is { } target && !IsRevealSatisfied(target))
        {
            Reveal(target);
            if (!forceApply)
            {
                StatusMessage = $"Showing {DescribeSurface(target.Surface)} — press Ctrl+Y again to redo";
                return false;
            }
        }

        _isApplyingHistory = true;
        try
        {
            var description = _history.RedoDescription;
            _history.Redo(this);
            if (action.RevealTarget is { } after)
            {
                Reveal(after);
            }

            if (!forceApply)
            {
                StatusMessage = $"Redid {description}";
            }

            return true;
        }
        finally
        {
            _isApplyingHistory = false;
        }
    }

    bool IUndoRecorder.IsApplyingHistory => _isApplyingHistory;

    void IUndoRecorder.Record(IUndoAction action) => RecordUndo(action);

    private void RecordUndo(IUndoAction undoInstruction)
    {
        if (_isApplyingHistory)
        {
            return;
        }

        _history.Push(undoInstruction);
    }

    private void RefreshHistoryMenus()
    {
        UndoMenuItems.Clear();
        var undoDepth = 0;
        foreach (var description in _history.GetUndoDescriptions())
        {
            undoDepth++;
            UndoMenuItems.Add(new HistoryMenuItem(undoDepth, description));
        }

        RedoMenuItems.Clear();
        var redoDepth = 0;
        foreach (var description in _history.GetRedoDescriptions())
        {
            redoDepth++;
            RedoMenuItems.Add(new HistoryMenuItem(redoDepth, description));
        }
    }

    private bool IsRevealSatisfied(UndoRevealTarget target) =>
        ReferenceEquals(SelectedRequest, target.Request)
        && SelectedRequestTabIndex == ToTabIndex(target.Surface);

    private void Reveal(UndoRevealTarget target)
    {
        SelectedNode = target.Request;
        SelectedRequestTabIndex = ToTabIndex(target.Surface);
    }

    private static int ToTabIndex(EditorSurface surface) =>
        surface switch
        {
            EditorSurface.Params => 0,
            EditorSurface.Body => 1,
            EditorSurface.Headers => 2,
            EditorSurface.Auth => 3,
            _ => 0
        };

    private static string DescribeSurface(EditorSurface surface) =>
        surface switch
        {
            EditorSurface.Params => "Params",
            EditorSurface.Body => "Body",
            EditorSurface.Headers => "Headers",
            EditorSurface.Auth => "Auth",
            _ => "editor"
        };

    /// <summary>Commits a title rename as a single undoable action (not per-character).</summary>
    public void CommitTitleRename(TreeNodeViewModel node, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(node.Name, newName, StringComparison.Ordinal))
        {
            return;
        }

        var previous = node.Name;
        SetName(node, newName);
        RecordUndo(new SetNameAction(node, previous, $"Rename '{newName}'"));
        OnPropertyChanged(nameof(SelectionSummary));
        StatusMessage = $"Renamed to '{newName}' (Ctrl+S to persist)";
    }

    void IUndoContext.SetName(TreeNodeViewModel node, string name) => SetName(node, name);

    void IUndoContext.RemoveNode(TreeNodeViewModel node) => RemoveNodeCore(node);

    void IUndoContext.InsertNode(TreeNodeViewModel? parent, TreeNodeViewModel node, int index) =>
        InsertNodeCore(parent, node, index);

    void IUndoContext.Select(TreeNodeViewModel? node) => SelectedNode = node;

    private static void SetName(TreeNodeViewModel node, string name)
    {
        node.Name = name;
    }

    private void WireUndoRecorders()
    {
        foreach (var collection in Collections)
        {
            WireUndoRecorders(collection);
        }
    }

    private void WireUndoRecorders(TreeNodeViewModel node)
    {
        if (node is RequestNodeViewModel request)
        {
            request.AttachUndoRecorder(this);
        }

        foreach (var child in node.Children)
        {
            WireUndoRecorders(child);
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
                WireUndoRecorders();
                StatusMessage = $"Seeded demo collections → {_persistence.StorageDirectory}";
                return;
            }

            foreach (var definition in loaded)
            {
                Collections.Add(CollectionTreeMapper.ToViewModel(definition));
            }

            WireUndoRecorders();
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

            WireUndoRecorders();
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

    [RelayCommand(CanExecute = nameof(CanSave))]
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

    private bool CanSave() =>
        CollectionTreeMapper.FindRootCollection(SelectedNode) is not null
        || Collections.Count > 0;

    [RelayCommand(CanExecute = nameof(CanSaveAll))]
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

    private bool CanSaveAll() => Collections.Count > 0;

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

    [RelayCommand(CanExecute = nameof(CanDuplicateRequest))]
    private void DuplicateRequest()
    {
        if (SelectedRequest is null)
        {
            return;
        }

        var source = SelectedRequest.Model;
        var copy = new RequestDefinition
        {
            Name = $"{source.Name} Copy",
            Verb = source.Verb,
            Address = source.Address,
            Authentication = new AuthenticationSettings
            {
                Type = source.Authentication.Type,
                BearerToken = source.Authentication.BearerToken,
                BasicUsername = source.Authentication.BasicUsername,
                BasicPassword = source.Authentication.BasicPassword,
                ApiKeyName = source.Authentication.ApiKeyName,
                ApiKeyValue = source.Authentication.ApiKeyValue,
                ApiKeyLocation = source.Authentication.ApiKeyLocation
            },
            Body = new RequestBody
            {
                Content = source.Body.Content,
                ContentType = source.Body.ContentType
            }
        };

        foreach (var p in source.Params)
        {
            copy.Params.Add(new KeyValueItem
            {
                IsEnabled = p.IsEnabled,
                Key = p.Key,
                Value = p.Value
            });
        }

        foreach (var h in source.Headers)
        {
            copy.Headers.Add(new KeyValueItem
            {
                IsEnabled = h.IsEnabled,
                Key = h.Key,
                Value = h.Value
            });
        }

        var node = new RequestNodeViewModel(copy);
        node.AttachUndoRecorder(this);
        var parent = SelectedRequest.Parent;
        switch (parent)
        {
            case CollectionNodeViewModel collection:
                collection.Model.Requests.Add(copy);
                collection.AddChild(node);
                break;
            case FolderNodeViewModel folder:
                folder.Model.Requests.Add(copy);
                folder.AddChild(node);
                break;
            default:
                return;
        }

        SelectedNode = node;
        RecordUndo(CreateRemoveAction(node, $"Duplicate '{source.Name}'"));
        StatusMessage = $"Duplicated '{source.Name}' (Ctrl+S to persist)";
    }

    private bool CanDuplicateRequest() => SelectedRequest is not null;

    [RelayCommand(CanExecute = nameof(CanCopyNode))]
    private void CopyNode(TreeNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is null)
        {
            return;
        }

        PrepareNodeForExport(node);
        _treeClipboard = node switch
        {
            CollectionNodeViewModel collection =>
                TreeClipboardPayload.FromCollection(collection.Model, collection.Name),
            FolderNodeViewModel folder =>
                TreeClipboardPayload.FromFolder(folder.Model, folder.Name),
            RequestNodeViewModel request =>
                TreeClipboardPayload.FromRequest(request.Model, request.Name),
            _ => null
        };

        if (_treeClipboard is null)
        {
            return;
        }

        _treeClipboard.WriteToClipboard();
        OnPropertyChanged(nameof(HasTreeClipboard));
        PasteIntoNodeCommand.NotifyCanExecuteChanged();
        PasteAtRootCommand.NotifyCanExecuteChanged();
        StatusMessage = $"Copied '{node.Name}'";
    }

    private bool CanCopyNode(TreeNodeViewModel? node) => (node ?? SelectedNode) is not null;

    [RelayCommand(CanExecute = nameof(CanPasteIntoNode))]
    private void PasteIntoNode(TreeNodeViewModel? target)
    {
        target ??= SelectedNode;
        if (target is not (CollectionNodeViewModel or FolderNodeViewModel) || !TryGetClipboard(out var payload))
        {
            return;
        }

        var pasted = PastePayloadIntoParent(payload, target);
        if (pasted is null)
        {
            return;
        }

        target.IsExpanded = true;
        SelectedNode = pasted;
        RecordUndo(CreateRemoveAction(pasted, $"Paste '{payload.SourceName}'"));
        StatusMessage = $"Pasted '{payload.SourceName}' into '{target.Name}' (Ctrl+S to persist)";
    }

    private bool CanPasteIntoNode(TreeNodeViewModel? target)
    {
        if (!HasTreeClipboard && !TreeClipboardPayload.TryReadFromClipboard(out _))
        {
            return false;
        }

        target ??= SelectedNode;
        return target is CollectionNodeViewModel or FolderNodeViewModel;
    }

    [RelayCommand(CanExecute = nameof(CanPasteAtRoot))]
    private void PasteAtRoot()
    {
        if (!TryGetClipboard(out var payload))
        {
            return;
        }

        var pasted = PastePayloadAtRoot(payload);
        if (pasted is null)
        {
            return;
        }

        SelectedNode = pasted;
        RecordUndo(CreateRemoveAction(pasted, $"Paste '{payload.SourceName}' at top level"));
        StatusMessage = $"Pasted '{payload.SourceName}' as top-level collection (Ctrl+S to persist)";
    }

    private bool CanPasteAtRoot() =>
        HasTreeClipboard || TreeClipboardPayload.TryReadFromClipboard(out _);

    [RelayCommand(CanExecute = nameof(CanSmartPaste))]
    private void SmartPaste()
    {
        if (CanPasteIntoNode(SelectedNode))
        {
            PasteIntoNode(SelectedNode);
        }
        else if (CanPasteAtRoot())
        {
            PasteAtRoot();
        }
    }

    private bool CanSmartPaste() => CanPasteIntoNode(SelectedNode) || CanPasteAtRoot();

    private bool TryGetClipboard(out TreeClipboardPayload payload)
    {
        if (_treeClipboard is not null)
        {
            payload = _treeClipboard;
            return true;
        }

        if (TreeClipboardPayload.TryReadFromClipboard(out var fromSystem) && fromSystem is not null)
        {
            _treeClipboard = fromSystem;
            OnPropertyChanged(nameof(HasTreeClipboard));
            payload = fromSystem;
            return true;
        }

        payload = null!;
        return false;
    }

    private TreeNodeViewModel? PastePayloadIntoParent(TreeClipboardPayload payload, TreeNodeViewModel parent)
    {
        return payload.Kind switch
        {
            TreeNodeKind.Request when payload.Request is not null =>
                AttachPastedRequest(parent, TreeNodeCloneService.CloneRequest(payload.Request)),
            TreeNodeKind.Folder when payload.Folder is not null =>
                AttachPastedFolder(parent, TreeNodeCloneService.CloneFolder(payload.Folder)),
            TreeNodeKind.Collection when payload.Collection is not null =>
                AttachPastedFolder(parent, TreeNodeCloneService.CollectionAsFolder(payload.Collection)),
            _ => null
        };
    }

    private TreeNodeViewModel? PastePayloadAtRoot(TreeClipboardPayload payload)
    {
        CollectionNodeViewModel node;
        switch (payload.Kind)
        {
            case TreeNodeKind.Collection when payload.Collection is not null:
                node = CollectionTreeMapper.ToViewModel(TreeNodeCloneService.CloneCollection(payload.Collection));
                break;
            case TreeNodeKind.Folder when payload.Folder is not null:
                node = CollectionTreeMapper.ToViewModel(TreeNodeCloneService.WrapInNewCollection(payload.Folder));
                break;
            case TreeNodeKind.Request when payload.Request is not null:
                node = CollectionTreeMapper.ToViewModel(TreeNodeCloneService.WrapInNewCollection(payload.Request));
                break;
            default:
                return null;
        }

        WireUndoRecorders(node);
        Collections.Add(node);
        node.IsExpanded = true;
        return node;
    }

    private RequestNodeViewModel AttachPastedRequest(TreeNodeViewModel parent, RequestDefinition model)
    {
        var node = new RequestNodeViewModel(model);
        WireUndoRecorders(node);
        switch (parent)
        {
            case CollectionNodeViewModel collection:
                collection.Model.Requests.Add(model);
                collection.AddChild(node);
                break;
            case FolderNodeViewModel folder:
                folder.Model.Requests.Add(model);
                folder.AddChild(node);
                break;
        }

        return node;
    }

    private FolderNodeViewModel AttachPastedFolder(TreeNodeViewModel parent, FolderDefinition model)
    {
        var node = CollectionTreeMapper.ToFolderViewModel(model);
        WireUndoRecorders(node);
        switch (parent)
        {
            case CollectionNodeViewModel collection:
                collection.Model.Folders.Add(model);
                collection.AddChild(node);
                break;
            case FolderNodeViewModel folder:
                folder.Model.Folders.Add(model);
                folder.AddChild(node);
                break;
        }

        return node;
    }

    [RelayCommand]
    private void OpenStorageFolder()
    {
        try
        {
            _persistence.EnsureStorageDirectory();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_persistence.StorageDirectory}\"",
                UseShellExecute = true
            });
            StatusMessage = "Opened collections folder";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Open Folder Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ShowAbout()
    {
        var dialog = new Views.AboutDialog(_persistence.StorageDirectory)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void Exit()
    {
        Application.Current?.MainWindow?.Close();
    }

    [RelayCommand]
    private void ExportNode(TreeNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is null)
        {
            return;
        }

        try
        {
            PrepareNodeForExport(node);
            var document = node switch
            {
                CollectionNodeViewModel collection => PostmanCollectionMapper.ToCollectionDocument(collection.Model),
                FolderNodeViewModel folder => PostmanCollectionMapper.ToCollectionDocument(folder.Model),
                RequestNodeViewModel request => PostmanCollectionMapper.ToCollectionDocument(request.Model),
                _ => throw new InvalidOperationException("Unsupported node type.")
            };

            var safeName = string.Join("_", node.Name.Split(Path.GetInvalidFileNameChars()));
            var dialog = new SaveFileDialog
            {
                Title = "Export for Postman",
                Filter = "Postman Collection (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"{safeName}.postman_collection.json",
                DefaultExt = ".json",
                AddExtension = true
            };

            if (dialog.ShowDialog(Application.Current?.MainWindow) != true)
            {
                return;
            }

            var json = JsonSerializer.Serialize(document, PostmanCollectionMapper.JsonOptions);
            File.WriteAllText(dialog.FileName, json);
            StatusMessage = $"Exported '{node.Name}' for Postman";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ImportIntoNode(TreeNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is not (CollectionNodeViewModel or FolderNodeViewModel))
        {
            MessageBox.Show(
                "Import is only available for collections and folders.",
                "Import",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import Postman Collection",
                Filter = "Postman Collection (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(Application.Current?.MainWindow) != true)
            {
                return;
            }

            var json = File.ReadAllText(dialog.FileName);
            var document = JsonSerializer.Deserialize<PostmanCollectionDocument>(json, PostmanCollectionMapper.JsonOptions)
                           ?? throw new InvalidOperationException("File is not a valid Postman collection.");

            if (document.Item.Count == 0)
            {
                MessageBox.Show(
                    "The Postman collection has no items to import.",
                    "Import",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var folders = new List<FolderDefinition>();
            var requests = new List<RequestDefinition>();
            PostmanCollectionMapper.ApplyItems(document.Item, folders, requests);

            var importedNodes = new List<TreeNodeViewModel>();
            switch (node)
            {
                case CollectionNodeViewModel collection:
                    foreach (var folder in folders)
                    {
                        collection.Model.Folders.Add(folder);
                        var child = CollectionTreeMapper.ToFolderViewModel(folder);
                        WireUndoRecorders(child);
                        collection.AddChild(child);
                        importedNodes.Add(child);
                    }

                    foreach (var request in requests)
                    {
                        collection.Model.Requests.Add(request);
                        var child = new RequestNodeViewModel(request);
                        child.AttachUndoRecorder(this);
                        collection.AddChild(child);
                        importedNodes.Add(child);
                    }

                    collection.IsExpanded = true;
                    break;

                case FolderNodeViewModel folderNode:
                    foreach (var folder in folders)
                    {
                        folderNode.Model.Folders.Add(folder);
                        var child = CollectionTreeMapper.ToFolderViewModel(folder);
                        WireUndoRecorders(child);
                        folderNode.AddChild(child);
                        importedNodes.Add(child);
                    }

                    foreach (var request in requests)
                    {
                        folderNode.Model.Requests.Add(request);
                        var child = new RequestNodeViewModel(request);
                        child.AttachUndoRecorder(this);
                        folderNode.AddChild(child);
                        importedNodes.Add(child);
                    }

                    folderNode.IsExpanded = true;
                    break;
            }

            if (importedNodes.Count > 0)
            {
                // Undo removes imported nodes last→first so sibling indices stay valid.
                var undoSteps = importedNodes
                    .AsEnumerable()
                    .Reverse()
                    .Select(n => CreateRemoveAction(n, $"Import into '{node.Name}'"))
                    .ToList();
                RecordUndo(new CompositeUndoAction($"Import into '{node.Name}'", undoSteps));
            }

            SelectedNode = node;
            StatusMessage = $"Imported {importedNodes.Count} item(s) into '{node.Name}' (Ctrl+S to persist)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Import Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void PrepareNodeForExport(TreeNodeViewModel node)
    {
        switch (node)
        {
            case CollectionNodeViewModel collection:
                CollectionTreeMapper.SyncModelFromView(collection);
                break;
            case FolderNodeViewModel folder:
                CollectionTreeMapper.SyncFolder(folder);
                break;
            case RequestNodeViewModel:
                // Request model is kept in sync by bindings.
                break;
        }
    }

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
        RecordUndo(CreateRemoveAction(node, $"Add collection '{name}'"));
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
        RecordUndo(CreateRemoveAction(node, $"Add folder '{name}'"));
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
        node.AttachUndoRecorder(this);

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
        RecordUndo(CreateRemoveAction(node, $"Add request '{name}'"));
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

        CommitTitleRename(SelectedNode, name);
    }

    private bool CanRenameNode() => SelectedNode is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteNode))]
    private void DeleteNode(TreeNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is null)
        {
            return;
        }

        var name = node.Name;
        var childCount = node.CountDescendants();
        var message = childCount > 0
            ? $"Delete '{name}' and its {childCount} child item(s)?"
            : $"Delete '{name}'?";

        var result = MessageBox.Show(
            message,
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var parent = node.Parent;
        var index = parent is not null
            ? parent.Children.IndexOf(node)
            : Collections.IndexOf((CollectionNodeViewModel)node);

        var toSelect = parent;
        if (ReferenceEquals(SelectedNode, node) || IsUnder(SelectedNode, node))
        {
            SelectedNode = toSelect;
        }

        RemoveNodeCore(node);
        RecordUndo(new InsertNodeAction(parent, node, index, $"Delete '{name}'"));
        StatusMessage = childCount > 0
            ? $"Deleted '{name}' and {childCount} child item(s)"
            : $"Deleted '{name}'";
    }

    private bool CanDeleteNode(TreeNodeViewModel? node) => (node ?? SelectedNode) is not null;

    private static bool IsUnder(TreeNodeViewModel? candidate, TreeNodeViewModel ancestor)
    {
        while (candidate is not null)
        {
            if (ReferenceEquals(candidate, ancestor))
            {
                return true;
            }

            candidate = candidate.Parent;
        }

        return false;
    }

    private RemoveNodeAction CreateRemoveAction(TreeNodeViewModel node, string description)
    {
        if (node is CollectionNodeViewModel collection)
        {
            return new RemoveNodeAction(node, null, Collections.IndexOf(collection), description);
        }

        var parent = node.Parent
                     ?? throw new InvalidOperationException("Non-collection node must have a parent.");
        return new RemoveNodeAction(node, parent, parent.Children.IndexOf(node), description);
    }

    private void RemoveNodeCore(TreeNodeViewModel node)
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
                if (!_isApplyingHistory)
                {
                    MessageBox.Show(
                        $"Collection removed from workspace, but the file could not be deleted.\n\n{ex.Message}",
                        "Delete Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            return;
        }

        var parent = node.Parent;
        if (parent is null)
        {
            return;
        }

        DetachFromModel(parent, node);
        parent.RemoveChild(node);
    }

    private void InsertNodeCore(TreeNodeViewModel? parent, TreeNodeViewModel node, int index)
    {
        if (node is CollectionNodeViewModel collection)
        {
            if (index < 0 || index > Collections.Count)
            {
                index = Collections.Count;
            }

            Collections.Insert(index, collection);
            try
            {
                // Restore on-disk file if this collection was previously persisted/deleted.
                CollectionTreeMapper.SyncModelFromView(collection);
                _persistence.Save(collection.Model);
            }
            catch
            {
                // Keep workspace restore even if disk write fails; user can Ctrl+S.
            }

            return;
        }

        if (parent is null)
        {
            return;
        }

        AttachToModel(parent, node, index);
        parent.InsertChild(node, index);
    }

    private static void DetachFromModel(TreeNodeViewModel parent, TreeNodeViewModel node)
    {
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
    }

    private static void AttachToModel(TreeNodeViewModel parent, TreeNodeViewModel node, int childIndex)
    {
        switch (parent)
        {
            case CollectionNodeViewModel parentCollection when node is FolderNodeViewModel folder:
                InsertModelItem(parentCollection.Model.Folders, folder.Model, CountKindBefore(parent, childIndex, TreeNodeKind.Folder));
                break;
            case CollectionNodeViewModel parentCollection when node is RequestNodeViewModel request:
                InsertModelItem(parentCollection.Model.Requests, request.Model, CountKindBefore(parent, childIndex, TreeNodeKind.Request));
                break;
            case FolderNodeViewModel parentFolder when node is FolderNodeViewModel folder:
                InsertModelItem(parentFolder.Model.Folders, folder.Model, CountKindBefore(parent, childIndex, TreeNodeKind.Folder));
                break;
            case FolderNodeViewModel parentFolder when node is RequestNodeViewModel request:
                InsertModelItem(parentFolder.Model.Requests, request.Model, CountKindBefore(parent, childIndex, TreeNodeKind.Request));
                break;
        }
    }

    private static int CountKindBefore(TreeNodeViewModel parent, int childIndex, TreeNodeKind kind)
    {
        var count = 0;
        var limit = Math.Clamp(childIndex, 0, parent.Children.Count);
        for (var i = 0; i < limit; i++)
        {
            if (parent.Children[i].Kind == kind)
            {
                count++;
            }
        }

        return count;
    }

    private static void InsertModelItem<T>(List<T> list, T item, int index)
    {
        if (list.Contains(item))
        {
            return;
        }

        if (index < 0 || index > list.Count)
        {
            list.Add(item);
        }
        else
        {
            list.Insert(index, item);
        }
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
