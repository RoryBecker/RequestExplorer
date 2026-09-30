using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RequestExplorer.Models;
using RequestExplorer.Services;
using RequestExplorer.Services.Export;
using RequestExplorer.Services.Postman;
using RequestExplorer.Services.Undo;

namespace RequestExplorer.ViewModels;

public partial class MainViewModel : ObservableObject, IUndoContext, IUndoRecorder
{
    private readonly CollectionPersistenceService _persistence;
    private readonly HttpRequestExecutor _httpExecutor;
    private readonly UiPreferencesService _uiPreferencesService;
    private readonly UndoStack _history = new();
    private CancellationTokenSource? _sendCts;
    private bool _isApplyingHistory;

    public MainViewModel()
        : this(new CollectionPersistenceService(), new HttpRequestExecutor(), new UiPreferencesService())
    {
    }

    public MainViewModel(
        CollectionPersistenceService persistence,
        HttpRequestExecutor httpExecutor,
        UiPreferencesService? uiPreferencesService = null)
    {
        _persistence = persistence;
        _httpExecutor = httpExecutor;
        _uiPreferencesService = uiPreferencesService ?? new UiPreferencesService();
        UiPreferences = _uiPreferencesService.Load();
        _isQuickExportVisible = UiPreferences.IsQuickExportVisible;
        _selectedExportFormat = ResolveExportFormat(UiPreferences.SelectedExportFormat);
        _isQuickExportWrap = UiPreferences.GetQuickExportWrap(_selectedExportFormat.Format.ToString());

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

    public UiPreferences UiPreferences { get; }

    public void PersistUiPreferences()
    {
        UiPreferences.IsQuickExportVisible = IsQuickExportVisible;
        UiPreferences.SelectedExportFormat = SelectedExportFormat.Format.ToString();
        _uiPreferencesService.Save(UiPreferences);
    }

    private static RequestExportFormatOption ResolveExportFormat(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)
            && Enum.TryParse<RequestExportFormat>(name, ignoreCase: true, out var format))
        {
            var match = RequestExportService.Formats.FirstOrDefault(f => f.Format == format);
            if (match is not null)
            {
                return match;
            }
        }

        return RequestExportService.Formats[0];
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
    [NotifyCanExecuteChangedFor(nameof(DuplicateNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(PasteIntoNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(PasteAtRootCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiagnoseVariablesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CollapseChildrenCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExpandChildrenCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveNodeUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveNodeDownCommand))]
    private TreeNodeViewModel? _selectedNode;

    private TreeClipboardPayload? _treeClipboard;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendRequestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSendCommand))]
    private bool _isSending;

    /// <summary>0=Params, 1=Body, 2=Headers, 3=Auth</summary>
    [ObservableProperty]
    private int _selectedRequestTabIndex;

    [ObservableProperty]
    private bool _isRootDropIndicatorVisible;

    [ObservableProperty]
    private bool _isQuickExportVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuickExportTextWrapping))]
    [NotifyPropertyChangedFor(nameof(QuickExportHorizontalScrollBarVisibility))]
    private bool _isQuickExportWrap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedExportFormatKind))]
    private RequestExportFormatOption _selectedExportFormat = RequestExportService.Formats[0];

    [ObservableProperty]
    private string _quickExportText = "Select a request to preview an export.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuickExportDiagnosis))]
    private string _quickExportDiagnosis = string.Empty;

    public bool HasQuickExportDiagnosis => !string.IsNullOrWhiteSpace(QuickExportDiagnosis);

    public TextWrapping QuickExportTextWrapping =>
        IsQuickExportWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public ScrollBarVisibility QuickExportHorizontalScrollBarVisibility =>
        IsQuickExportWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

    public IReadOnlyList<RequestExportFormatOption> ExportFormats => RequestExportService.Formats;

    /// <summary>ComboBox-friendly format value; persists via <see cref="SelectedExportFormat"/>.</summary>
    public RequestExportFormat SelectedExportFormatKind
    {
        get => SelectedExportFormat.Format;
        set
        {
            if (SelectedExportFormat.Format == value)
            {
                return;
            }

            SelectedExportFormat = ResolveExportFormat(value.ToString());
        }
    }

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
        if (oldValue is RequestNodeViewModel oldRequest)
        {
            DetachRequestExportWatchers(oldRequest);
        }

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

        if (newValue is RequestNodeViewModel newRequest)
        {
            AttachRequestExportWatchers(newRequest);
        }

        CopyNodeCommand.NotifyCanExecuteChanged();
        PasteIntoNodeCommand.NotifyCanExecuteChanged();
        PasteAtRootCommand.NotifyCanExecuteChanged();
        SmartPasteCommand.NotifyCanExecuteChanged();
        RefreshQuickExport();
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

        if (sender is RequestNodeViewModel)
        {
            RefreshQuickExport();
        }
    }

    partial void OnSelectedExportFormatChanged(RequestExportFormatOption value)
    {
        IsQuickExportWrap = UiPreferences.GetQuickExportWrap(value.Format.ToString());
        RefreshQuickExport();
        PersistUiPreferences();
    }

    partial void OnIsQuickExportVisibleChanged(bool value)
    {
        if (value)
        {
            RefreshQuickExport();
        }
    }

    partial void OnIsQuickExportWrapChanged(bool value)
    {
        UiPreferences.SetQuickExportWrap(SelectedExportFormat.Format.ToString(), value);
        PersistUiPreferences();
    }

    [RelayCommand]
    private void ToggleQuickExport()
    {
        IsQuickExportVisible = !IsQuickExportVisible;
        StatusMessage = IsQuickExportVisible ? "Quick Export shown" : "Quick Export hidden";
    }

    [RelayCommand]
    private void ToggleQuickExportWrap()
    {
        IsQuickExportWrap = !IsQuickExportWrap;
        StatusMessage = IsQuickExportWrap ? "Quick Export wrap on" : "Quick Export wrap off";
    }

    [RelayCommand(CanExecute = nameof(CanCopyQuickExport))]
    private void CopyQuickExport()
    {
        if (string.IsNullOrWhiteSpace(QuickExportText))
        {
            return;
        }

        Clipboard.SetText(QuickExportText);
        StatusMessage = "Export copied to clipboard";
    }

    private bool CanCopyQuickExport() =>
        IsQuickExportVisible && !string.IsNullOrWhiteSpace(QuickExportText);

    private void AttachRequestExportWatchers(RequestNodeViewModel request)
    {
        request.Params.CollectionChanged += OnRequestExportCollectionsChanged;
        request.Headers.CollectionChanged += OnRequestExportCollectionsChanged;
        foreach (var item in request.Params.Concat(request.Headers))
        {
            item.PropertyChanged += OnRequestExportItemChanged;
        }
    }

    private void DetachRequestExportWatchers(RequestNodeViewModel request)
    {
        request.Params.CollectionChanged -= OnRequestExportCollectionsChanged;
        request.Headers.CollectionChanged -= OnRequestExportCollectionsChanged;
        foreach (var item in request.Params.Concat(request.Headers))
        {
            item.PropertyChanged -= OnRequestExportItemChanged;
        }
    }

    private void OnRequestExportCollectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (KeyValueItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnRequestExportItemChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (KeyValueItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnRequestExportItemChanged;
            }
        }

        RefreshQuickExport();
    }

    private void OnRequestExportItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        RefreshQuickExport();

    private void RefreshQuickExport()
    {
        CopyQuickExportCommand.NotifyCanExecuteChanged();

        if (!IsQuickExportVisible)
        {
            return;
        }

        if (SelectedRequest is null)
        {
            QuickExportText = "Select a request to preview an export.";
            QuickExportDiagnosis = string.Empty;
            return;
        }

        try
        {
            var diagnosis = VariableDiagnostics.DiagnoseRequest(SelectedRequest);
            QuickExportDiagnosis = diagnosis.HasIssues
                ? VariableDiagnostics.FormatRequestReport(diagnosis)
                  + "\n\nDefine missing variables on a parent folder or collection (and ensure they are enabled)."
                : string.Empty;

            var resolved = VariableResolver.Resolve(SelectedRequest.Model, SelectedRequest);
            QuickExportText = RequestExportService.Export(resolved, SelectedExportFormat.Format);
        }
        catch (Exception ex)
        {
            QuickExportText = $"// Export failed: {ex.Message}";
            QuickExportDiagnosis = string.Empty;
        }

        CopyQuickExportCommand.NotifyCanExecuteChanged();
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

    private bool IsRevealSatisfied(UndoRevealTarget target)
    {
        if (!ReferenceEquals(SelectedNode, target.Node))
        {
            return false;
        }

        if (target.Node is RequestNodeViewModel
            && target.Surface is not EditorSurface.None and not EditorSurface.Variables)
        {
            return SelectedRequestTabIndex == ToTabIndex(target.Surface);
        }

        return true;
    }

    private void Reveal(UndoRevealTarget target)
    {
        SelectedNode = target.Node;
        if (target.Node is RequestNodeViewModel
            && target.Surface is not EditorSurface.None and not EditorSurface.Variables)
        {
            SelectedRequestTabIndex = ToTabIndex(target.Surface);
        }
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
            EditorSurface.Variables => "Variables",
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
        else if (node is ContainerNodeViewModel container)
        {
            container.AttachUndoRecorder(this);
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
                CollapseAllCollections();
                StatusMessage = $"Seeded demo collections → {_persistence.StorageDirectory}";
                return;
            }

            foreach (var definition in loaded)
            {
                Collections.Add(CollectionTreeMapper.ToViewModel(definition));
            }

            WireUndoRecorders();
            CollapseAllCollections();
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
            CollapseAllCollections();
            StatusMessage = "Load failed — using in-memory demo (not saved)";
        }
    }

    private void CollapseAllCollections()
    {
        foreach (var collection in Collections)
        {
            CollapseSubtree(collection);
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

        var diagnosis = VariableDiagnostics.DiagnoseRequest(SelectedRequest);
        if (diagnosis.HasIssues)
        {
            var message = VariableDiagnostics.FormatRequestReport(diagnosis)
                          + "\n\nDefine missing variables on a parent folder or collection (and ensure they are enabled), then send again.";
            Response.Apply(new HttpExecutionResult
            {
                Succeeded = false,
                ErrorMessage = message
            });
            StatusMessage = VariableDiagnostics.FormatRequestReport(diagnosis);
            return;
        }

        _sendCts?.Cancel();
        _sendCts?.Dispose();
        _sendCts = new CancellationTokenSource();

        IsSending = true;
        Response.BeginSend();
        var resolved = VariableResolver.Resolve(SelectedRequest.Model, SelectedRequest);
        StatusMessage = $"Sending {SelectedRequest.Verb} {resolved.Address}…";

        try
        {
            var result = await _httpExecutor.SendAsync(resolved, _sendCts.Token);
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

    [RelayCommand(CanExecute = nameof(CanDiagnoseVariables))]
    private void DiagnoseVariables()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var results = VariableDiagnostics.DiagnoseSubtree(SelectedNode);
        var report = VariableDiagnostics.FormatSubtreeReport(SelectedNode, results);
        var failing = results.Count(r => r.HasIssues);
        StatusMessage = failing == 0
            ? $"Diagnosis OK — {results.Count} request(s)"
            : $"Diagnosis: {failing} of {results.Count} request(s) have missing variables";

        var dialog = new Views.DiagnosisReportDialog("Variable Diagnosis", report)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }

    private bool CanDiagnoseVariables() => SelectedNode is not null;

    [RelayCommand(CanExecute = nameof(CanCancelSend))]
    private void CancelSend()
    {
        _sendCts?.Cancel();
        StatusMessage = "Cancelling…";
    }

    private bool CanCancelSend() => IsSending;

    [RelayCommand(CanExecute = nameof(CanDuplicateNode))]
    private void DuplicateNode()
    {
        if (SelectedNode is null)
        {
            return;
        }

        TreeNodeViewModel? cloneNode;
        var sourceName = SelectedNode.Name;

        switch (SelectedNode)
        {
            case RequestNodeViewModel request:
            {
                var copy = TreeNodeCloneService.CloneRequest(request.Model);
                copy.Name = $"{request.Name} Copy";
                cloneNode = new RequestNodeViewModel(copy);
                WireUndoRecorders(cloneNode);
                if (!TryInsertAsSiblingAfter(request, cloneNode))
                {
                    return;
                }

                break;
            }
            case FolderNodeViewModel folder:
            {
                var copy = TreeNodeCloneService.CloneFolder(folder.Model);
                copy.Name = $"{folder.Name} Copy";
                cloneNode = CollectionTreeMapper.ToFolderViewModel(copy);
                WireUndoRecorders(cloneNode);
                if (!TryInsertAsSiblingAfter(folder, cloneNode))
                {
                    return;
                }

                break;
            }
            case CollectionNodeViewModel collection:
            {
                var copy = TreeNodeCloneService.CloneCollection(collection.Model);
                copy.Name = $"{collection.Name} Copy";
                var collectionNode = CollectionTreeMapper.ToViewModel(copy);
                WireUndoRecorders(collectionNode);
                var insertAt = Collections.IndexOf(collection) + 1;
                if (insertAt < 1)
                {
                    insertAt = Collections.Count;
                }

                Collections.Insert(insertAt, collectionNode);
                cloneNode = collectionNode;
                break;
            }
            default:
                return;
        }

        SelectedNode = cloneNode;
        RecordUndo(CreateRemoveAction(cloneNode, $"Duplicate '{sourceName}'"));
        StatusMessage = $"Duplicated '{sourceName}' (Ctrl+S to persist)";
    }

    private bool CanDuplicateNode() => SelectedNode is not null;

    private bool TryInsertAsSiblingAfter(TreeNodeViewModel source, TreeNodeViewModel clone)
    {
        var parent = source.Parent;
        if (parent is not (CollectionNodeViewModel or FolderNodeViewModel))
        {
            return false;
        }

        var insertAt = parent.Children.IndexOf(source) + 1;
        if (insertAt < 1)
        {
            insertAt = parent.Children.Count;
        }

        AttachToModel(parent, clone, insertAt);
        parent.InsertChild(clone, insertAt);
        return true;
    }

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
        SmartPasteCommand.NotifyCanExecuteChanged();
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
        if (!TryGetClipboard(out var payload))
        {
            return;
        }

        switch (SelectedNode)
        {
            case CollectionNodeViewModel collection:
            {
                var pasted = PastePayloadIntoParent(payload, collection);
                if (pasted is null)
                {
                    return;
                }

                collection.IsExpanded = true;
                SelectedNode = pasted;
                RecordUndo(CreateRemoveAction(pasted, $"Paste '{payload.SourceName}'"));
                StatusMessage = $"Pasted '{payload.SourceName}' into '{collection.Name}' (Ctrl+S to persist)";
                break;
            }
            case FolderNodeViewModel folder:
            {
                var pasted = PastePayloadIntoParent(payload, folder);
                if (pasted is null)
                {
                    return;
                }

                folder.IsExpanded = true;
                SelectedNode = pasted;
                RecordUndo(CreateRemoveAction(pasted, $"Paste '{payload.SourceName}'"));
                StatusMessage = $"Pasted '{payload.SourceName}' into '{folder.Name}' (Ctrl+S to persist)";
                break;
            }
            case RequestNodeViewModel request when request.Parent is not null:
            {
                var pasted = PastePayloadAsSibling(payload, request);
                if (pasted is null)
                {
                    return;
                }

                SelectedNode = pasted;
                RecordUndo(CreateRemoveAction(pasted, $"Paste '{payload.SourceName}'"));
                StatusMessage = $"Pasted '{payload.SourceName}' next to '{request.Name}' (Ctrl+S to persist)";
                break;
            }
            default:
                if (CanPasteAtRoot())
                {
                    PasteAtRoot();
                }

                break;
        }
    }

    private bool CanSmartPaste()
    {
        if (!HasTreeClipboard && !TreeClipboardPayload.TryReadFromClipboard(out _))
        {
            return false;
        }

        return SelectedNode is CollectionNodeViewModel or FolderNodeViewModel
               || SelectedNode is RequestNodeViewModel { Parent: not null }
               || CanPasteAtRoot();
    }

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

    /// <summary>Paste beside <paramref name="sibling"/> under the same parent, preserving clipboard hierarchy.</summary>
    private TreeNodeViewModel? PastePayloadAsSibling(TreeClipboardPayload payload, TreeNodeViewModel sibling)
    {
        var parent = sibling.Parent;
        if (parent is not (CollectionNodeViewModel or FolderNodeViewModel))
        {
            return null;
        }

        TreeNodeViewModel? node = payload.Kind switch
        {
            TreeNodeKind.Request when payload.Request is not null =>
                CreatePastedRequestNode(TreeNodeCloneService.CloneRequest(payload.Request)),
            TreeNodeKind.Folder when payload.Folder is not null =>
                CreatePastedFolderNode(TreeNodeCloneService.CloneFolder(payload.Folder)),
            TreeNodeKind.Collection when payload.Collection is not null =>
                CreatePastedFolderNode(TreeNodeCloneService.CollectionAsFolder(payload.Collection)),
            _ => null
        };

        if (node is null)
        {
            return null;
        }

        var insertAt = parent.Children.IndexOf(sibling) + 1;
        if (insertAt < 1)
        {
            insertAt = parent.Children.Count;
        }

        AttachToModel(parent, node, insertAt);
        parent.InsertChild(node, insertAt);
        return node;
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

    private RequestNodeViewModel CreatePastedRequestNode(RequestDefinition model)
    {
        var node = new RequestNodeViewModel(model);
        WireUndoRecorders(node);
        return node;
    }

    private FolderNodeViewModel CreatePastedFolderNode(FolderDefinition model)
    {
        var node = CollectionTreeMapper.ToFolderViewModel(model);
        WireUndoRecorders(node);
        return node;
    }

    private RequestNodeViewModel AttachPastedRequest(TreeNodeViewModel parent, RequestDefinition model)
    {
        var node = CreatePastedRequestNode(model);
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
        var node = CreatePastedFolderNode(model);
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
                Title = "Import Postman Collection(s)",
                Filter = "Postman Collection (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = true
            };

            if (dialog.ShowDialog(Application.Current?.MainWindow) != true
                || dialog.FileNames.Length == 0)
            {
                return;
            }

            var importedNodes = new List<FolderNodeViewModel>();
            var errors = new List<string>();

            foreach (var filePath in dialog.FileNames.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var document = JsonSerializer.Deserialize<PostmanCollectionDocument>(
                                       json,
                                       PostmanCollectionMapper.JsonOptions)
                                   ?? throw new InvalidOperationException("File is not a valid Postman collection.");

                    var fileHint = PostmanCollectionMapper.ResolveImportNameFromFilePath(filePath);
                    var importedFolder = PostmanCollectionMapper.FromCollectionDocumentAsFolder(document, fileHint);
                    if (importedFolder.Folders.Count == 0
                        && importedFolder.Requests.Count == 0
                        && importedFolder.Variables.Count == 0)
                    {
                        errors.Add($"{Path.GetFileName(filePath)}: no items or variables to import.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(importedFolder.Name))
                    {
                        importedFolder.Name = string.IsNullOrWhiteSpace(fileHint) ? "Imported Collection" : fileHint;
                    }

                    var child = AttachImportedFolder(node, importedFolder);
                    importedNodes.Add(child);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            if (importedNodes.Count == 0)
            {
                MessageBox.Show(
                    errors.Count > 0
                        ? string.Join(Environment.NewLine, errors)
                        : "No Postman collections were imported.",
                    "Import",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            node.IsExpanded = true;
            node.IsDirty = true;
            foreach (var child in importedNodes)
            {
                CollapseSubtree(child);
                MarkSubtreeDirty(child);
            }

            var undoSteps = importedNodes
                .AsEnumerable()
                .Reverse()
                .Select(n => CreateRemoveAction(n, $"Import into '{node.Name}'"))
                .ToList();
            RecordUndo(new CompositeUndoAction(
                importedNodes.Count == 1
                    ? $"Import '{importedNodes[0].Name}' into '{node.Name}'"
                    : $"Import {importedNodes.Count} collections into '{node.Name}'",
                undoSteps));

            SelectedNode = importedNodes[^1];
            OnPropertyChanged(nameof(HasAnyDirty));
            StatusMessage = importedNodes.Count == 1
                ? $"Imported '{importedNodes[0].Name}' into '{node.Name}' (Ctrl+S to persist)"
                : $"Imported {importedNodes.Count} collections into '{node.Name}' (Ctrl+S to persist)";

            if (errors.Count > 0)
            {
                MessageBox.Show(
                    $"Imported {importedNodes.Count} collection(s), with {errors.Count} skipped:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}",
                    "Import",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
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

    private FolderNodeViewModel AttachImportedFolder(TreeNodeViewModel node, FolderDefinition importedFolder)
    {
        switch (node)
        {
            case CollectionNodeViewModel collection:
                collection.Model.Folders.Add(importedFolder);
                var collectionChild = CollectionTreeMapper.ToFolderViewModel(importedFolder);
                WireUndoRecorders(collectionChild);
                collection.AddChild(collectionChild);
                return collectionChild;

            case FolderNodeViewModel folderNode:
                folderNode.Model.Folders.Add(importedFolder);
                var folderChild = CollectionTreeMapper.ToFolderViewModel(importedFolder);
                WireUndoRecorders(folderChild);
                folderNode.AddChild(folderChild);
                return folderChild;

            default:
                throw new InvalidOperationException("Import target must be a collection or folder.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCollapseOrExpandChildren))]
    private void CollapseChildren(TreeNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is null || node.Children.Count == 0)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            CollapseSubtree(child);
        }

        StatusMessage = $"Collapsed children of '{node.Name}'";
    }

    [RelayCommand(CanExecute = nameof(CanCollapseOrExpandChildren))]
    private void ExpandChildren(TreeNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is null || node.Children.Count == 0)
        {
            return;
        }

        node.IsExpanded = true;
        foreach (var child in node.Children)
        {
            ExpandSubtree(child);
        }

        StatusMessage = $"Expanded children of '{node.Name}'";
    }

    private bool CanCollapseOrExpandChildren(TreeNodeViewModel? node)
    {
        node ??= SelectedNode;
        return node is CollectionNodeViewModel or FolderNodeViewModel
               && node.Children.Count > 0;
    }

    private static void CollapseSubtree(TreeNodeViewModel node)
    {
        node.IsExpanded = false;
        foreach (var child in node.Children)
        {
            CollapseSubtree(child);
        }
    }

    private static void ExpandSubtree(TreeNodeViewModel node)
    {
        if (node.Children.Count > 0)
        {
            node.IsExpanded = true;
        }

        foreach (var child in node.Children)
        {
            ExpandSubtree(child);
        }
    }

    private static void MarkSubtreeDirty(TreeNodeViewModel node)
    {
        node.IsDirty = true;
        foreach (var child in node.Children)
        {
            MarkSubtreeDirty(child);
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
        node.AttachUndoRecorder(this);
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
        node.AttachUndoRecorder(this);

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

        var confirmed = Views.ConfirmDialog.Confirm(
            Application.Current?.MainWindow,
            "Confirm Delete",
            message);

        if (!confirmed)
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

    public void ClearAllDropIndicators()
    {
        foreach (var collection in Collections)
        {
            ClearDropIndicators(collection);
        }

        IsRootDropIndicatorVisible = false;
    }

    private static void ClearDropIndicators(TreeNodeViewModel node)
    {
        node.DropIndicator = TreeDropIndicator.None;
        foreach (var child in node.Children)
        {
            ClearDropIndicators(child);
        }
    }

    /// <summary>Moves a node on drop. Returns true when the tree changed.</summary>
    public bool TryMoveNode(TreeNodeViewModel source, TreeNodeViewModel? target, TreeDropPlacement placement)
    {
        if (placement == TreeDropPlacement.Root || (target is null && placement == TreeDropPlacement.Root))
        {
            return MoveToRoot(source, index: 0);
        }

        if (target is null)
        {
            return false;
        }

        if (ReferenceEquals(source, target) || IsUnder(target, source))
        {
            return false;
        }

        return placement switch
        {
            TreeDropPlacement.Into => MoveInto(source, target),
            TreeDropPlacement.Before => MoveBeside(source, target, after: false),
            TreeDropPlacement.After => MoveBeside(source, target, after: true),
            _ => false
        };
    }

    [RelayCommand(CanExecute = nameof(CanMoveNodeUp))]
    private void MoveNodeUp()
    {
        if (TryMoveSelectedAmongSiblings(-1))
        {
            NotifyMoveAmongSiblingsCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanMoveNodeDown))]
    private void MoveNodeDown()
    {
        if (TryMoveSelectedAmongSiblings(1))
        {
            NotifyMoveAmongSiblingsCanExecuteChanged();
        }
    }

    private bool CanMoveNodeUp() => CanMoveSelectedAmongSiblings(-1);

    private bool CanMoveNodeDown() => CanMoveSelectedAmongSiblings(1);

    private void NotifyMoveAmongSiblingsCanExecuteChanged()
    {
        MoveNodeUpCommand.NotifyCanExecuteChanged();
        MoveNodeDownCommand.NotifyCanExecuteChanged();
    }

    private bool CanMoveSelectedAmongSiblings(int direction)
    {
        if (Keyboard.FocusedElement is TextBox || SelectedNode is null)
        {
            return false;
        }

        return TryGetSibling(SelectedNode, direction, out _, out _);
    }

    private bool TryMoveSelectedAmongSiblings(int direction)
    {
        if (SelectedNode is null
            || !TryGetSibling(SelectedNode, direction, out var sibling, out _))
        {
            return false;
        }

        // Up → place before the sibling above; Down → place after the sibling below.
        return MoveBeside(SelectedNode, sibling, after: direction > 0);
    }

    private bool TryGetSibling(
        TreeNodeViewModel node,
        int direction,
        out TreeNodeViewModel sibling,
        out int index)
    {
        sibling = null!;
        index = -1;

        if (node is CollectionNodeViewModel collection && node.Parent is null)
        {
            index = Collections.IndexOf(collection);
            var next = index + direction;
            if (index < 0 || next < 0 || next >= Collections.Count)
            {
                return false;
            }

            sibling = Collections[next];
            return true;
        }

        var parent = node.Parent;
        if (parent is null)
        {
            return false;
        }

        index = parent.Children.IndexOf(node);
        var nextIndex = index + direction;
        if (index < 0 || nextIndex < 0 || nextIndex >= parent.Children.Count)
        {
            return false;
        }

        sibling = parent.Children[nextIndex];
        return true;
    }

    private bool MoveInto(TreeNodeViewModel source, TreeNodeViewModel target)
    {
        if (target is not (CollectionNodeViewModel or FolderNodeViewModel))
        {
            return false;
        }

        if (source is CollectionNodeViewModel collection)
        {
            return MoveCollectionAsFolder(collection, target, target.Children.Count);
        }

        return MoveIdentity(source, target, target.Children.Count);
    }

    private bool MoveBeside(TreeNodeViewModel source, TreeNodeViewModel target, bool after)
    {
        if (target is CollectionNodeViewModel && source is CollectionNodeViewModel)
        {
            var index = Collections.IndexOf((CollectionNodeViewModel)target);
            if (index < 0)
            {
                return false;
            }

            if (after)
            {
                index++;
            }

            return MoveIdentity(source, parent: null, index);
        }

        if (target is CollectionNodeViewModel)
        {
            // Folder/request beside a root collection → wrap as new collection at that root slot.
            var index = Collections.IndexOf((CollectionNodeViewModel)target);
            if (index < 0)
            {
                return false;
            }

            if (after)
            {
                index++;
            }

            return MoveToRoot(source, index);
        }

        var parent = target.Parent;
        if (parent is null)
        {
            return false;
        }

        var siblingIndex = parent.Children.IndexOf(target);
        if (siblingIndex < 0)
        {
            return false;
        }

        if (after)
        {
            siblingIndex++;
        }

        if (source is CollectionNodeViewModel collection)
        {
            return MoveCollectionAsFolder(collection, parent, siblingIndex);
        }

        return MoveIdentity(source, parent, siblingIndex);
    }

    private void RefreshRowLayoutAfterMove(TreeNodeViewModel node)
    {
        node.NotifyRowLayoutChanged();
        // Re-measure after the item is in its new visual indent.
        Application.Current?.Dispatcher.BeginInvoke(
            () => node.NotifyRowLayoutChanged(),
            DispatcherPriority.Loaded);
    }

    private bool MoveToRoot(TreeNodeViewModel source, int index)
    {
        if (source is CollectionNodeViewModel)
        {
            return MoveIdentity(source, parent: null, index);
        }

        // Folder or request → create containing collection (move contents).
        PrepareNodeForExport(source);
        CollectionDefinition collectionModel;
        switch (source)
        {
            case FolderNodeViewModel folder:
                CollectionTreeMapper.SyncFolder(folder);
                collectionModel = TreeNodeCloneService.WrapInNewCollection(folder.Model);
                break;
            case RequestNodeViewModel request:
                collectionModel = TreeNodeCloneService.WrapInNewCollection(request.Model);
                break;
            default:
                return false;
        }

        var oldParent = source.Parent;
        var oldIndex = oldParent is not null
            ? oldParent.Children.IndexOf(source)
            : -1;

        RemoveNodeCore(source);

        if (index < 0 || index > Collections.Count)
        {
            index = Collections.Count;
        }

        var newNode = CollectionTreeMapper.ToViewModel(collectionModel);
        WireUndoRecorders(newNode);
        InsertNodeCore(null, newNode, index);
        newNode.IsExpanded = true;
        SelectedNode = newNode;

        RefreshRowLayoutAfterMove(newNode);

        RecordUndo(new CompositeUndoAction(
            $"Move '{source.Name}' to top level",
            [
                new RemoveNodeAction(newNode, null, index, $"Move '{source.Name}' to top level"),
                new InsertNodeAction(oldParent, source, oldIndex, $"Move '{source.Name}' to top level")
            ]));

        StatusMessage = $"Moved '{source.Name}' to top level (Ctrl+S to persist)";
        return true;
    }

    private bool MoveCollectionAsFolder(CollectionNodeViewModel collection, TreeNodeViewModel parent, int index)
    {
        CollectionTreeMapper.SyncModelFromView(collection);
        var folderModel = TreeNodeCloneService.CollectionAsFolder(collection.Model);
        var oldIndex = Collections.IndexOf(collection);
        if (oldIndex < 0)
        {
            return false;
        }

        RemoveNodeCore(collection);

        if (ReferenceEquals(parent, collection) || IsUnder(parent, collection))
        {
            // Should never happen after remove, but keep safe.
        }

        var folderNode = CollectionTreeMapper.ToFolderViewModel(folderModel);
        WireUndoRecorders(folderNode);

        // Adjust index if inserting into a list that didn't contain the collection.
        index = Math.Clamp(index, 0, parent.Children.Count);
        InsertNodeCore(parent, folderNode, index);
        parent.IsExpanded = true;
        SelectedNode = folderNode;

        RefreshRowLayoutAfterMove(folderNode);

        RecordUndo(new CompositeUndoAction(
            $"Move collection '{collection.Name}' into '{parent.Name}'",
            [
                new RemoveNodeAction(folderNode, parent, index, $"Move '{collection.Name}'"),
                new InsertNodeAction(null, collection, oldIndex, $"Move '{collection.Name}'")
            ]));

        StatusMessage = $"Moved '{collection.Name}' into '{parent.Name}' as a folder (Ctrl+S to persist)";
        return true;
    }

    private bool MoveIdentity(TreeNodeViewModel source, TreeNodeViewModel? parent, int index)
    {
        var oldParent = source.Parent;
        var oldIndex = oldParent is not null
            ? oldParent.Children.IndexOf(source)
            : Collections.IndexOf((CollectionNodeViewModel)source);

        if (oldIndex < 0 && source is not CollectionNodeViewModel)
        {
            return false;
        }

        // Same parent: adjust index when removing an earlier sibling.
        if (ReferenceEquals(oldParent, parent) && oldIndex >= 0 && oldIndex < index)
        {
            index--;
        }

        if (parent is null && source is CollectionNodeViewModel
            && oldParent is null && oldIndex == index)
        {
            return false;
        }

        if (ReferenceEquals(oldParent, parent) && oldIndex == index)
        {
            return false;
        }

        // Moving into own current parent at same logical place after adjust
        RemoveNodeCore(source);

        if (parent is null)
        {
            if (source is not CollectionNodeViewModel)
            {
                // Should have gone through MoveToRoot
                return false;
            }

            index = Math.Clamp(index, 0, Collections.Count);
        }
        else
        {
            index = Math.Clamp(index, 0, parent.Children.Count);
        }

        InsertNodeCore(parent, source, index);
        if (parent is not null)
        {
            parent.IsExpanded = true;
        }

        SelectedNode = source;

        RefreshRowLayoutAfterMove(source);

        RecordUndo(new CompositeUndoAction(
            $"Move '{source.Name}'",
            [
                new RemoveNodeAction(source, parent, index, $"Move '{source.Name}'"),
                new InsertNodeAction(oldParent, source, oldIndex, $"Move '{source.Name}'")
            ]));

        StatusMessage = $"Moved '{source.Name}' (Ctrl+S to persist)";
        return true;
    }

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

        var choice = Views.ConfirmSaveDialog.Prompt(Application.Current?.MainWindow);

        switch (choice)
        {
            case Views.ConfirmSaveChoice.Cancel:
                return false;
            case Views.ConfirmSaveChoice.Discard:
                return true;
            case Views.ConfirmSaveChoice.Save:
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
