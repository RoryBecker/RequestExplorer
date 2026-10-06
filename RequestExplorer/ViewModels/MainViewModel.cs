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
    private readonly WorkspaceBackupService _backups;
    private readonly UndoStack _history = new();
    private CancellationTokenSource? _sendCts;
    private bool _isApplyingHistory;
    /// <summary>Blocks incidental undo records (e.g. binding writes while selecting imported nodes).</summary>
    private bool _suppressUndoRecording;
    private int _backupBusy;

    public MainViewModel()
        : this(new CollectionPersistenceService(), new HttpRequestExecutor(), new UiPreferencesService())
    {
    }

    public MainViewModel(
        CollectionPersistenceService persistence,
        HttpRequestExecutor httpExecutor,
        UiPreferencesService? uiPreferencesService = null,
        WorkspaceBackupService? backupService = null)
    {
        _persistence = persistence;
        _httpExecutor = httpExecutor;
        _uiPreferencesService = uiPreferencesService ?? new UiPreferencesService();
        _backups = backupService ?? new WorkspaceBackupService(
            persistence.StorageDirectory,
            _uiPreferencesService.FilePath);
        UiPreferences = _uiPreferencesService.Load();
        if (UiPreferences.RequestTimeoutSeconds is < 1 or > 3600)
        {
            UiPreferences.RequestTimeoutSeconds = HttpRequestExecutor.DefaultTimeoutSeconds;
        }

        UiPreferences.BackupRetentionCount =
            WorkspaceBackupService.ClampRetention(UiPreferences.BackupRetentionCount);
        UiPreferences.RunAllMaxThreads = Math.Clamp(UiPreferences.RunAllMaxThreads, 1, 10);

        _isQuickExportVisible = UiPreferences.IsQuickExportVisible;
        _isDarkMode = UiPreferences.IsDarkMode;
        ThemeService.Apply(_isDarkMode);
        _selectedExportFormat = ResolveExportFormat(UiPreferences.SelectedExportFormat);
        _isQuickExportWrap = UiPreferences.GetQuickExportWrap(_selectedExportFormat.Format.ToString());
        _treeFilterIncludeAncestors = UiPreferences.TreeFilterIncludeAncestors;
        _treeFilterIncludeName = UiPreferences.TreeFilterIncludeName;
        _treeFilterIncludeUrl = UiPreferences.TreeFilterIncludeUrl;
        EnsureValidTreeSearchScope();
        SyncTreeSearchScope();
        ApplyHttpTimeoutSettings();

        Collections.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCollections));
            ApplyTreeFilterIfActive();
            PersistCollectionOrder();
        };
        TreeNodeViewModel.SearchTextInvalidated += ApplyTreeFilterIfActive;
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

    private void ApplyHttpTimeoutSettings() =>
        _httpExecutor.ApplyTimeoutSettings(
            UiPreferences.RequestTimeoutSeconds,
            UiPreferences.DevModeIgnoreTimeout);

    public UiPreferences UiPreferences { get; }

    public void PersistUiPreferences()
    {
        UiPreferences.IsQuickExportVisible = IsQuickExportVisible;
        UiPreferences.IsDarkMode = IsDarkMode;
        UiPreferences.SelectedExportFormat = SelectedExportFormat.Format.ToString();
        UiPreferences.TreeFilterIncludeAncestors = TreeFilterIncludeAncestors;
        UiPreferences.TreeFilterIncludeName = TreeFilterIncludeName;
        UiPreferences.TreeFilterIncludeUrl = TreeFilterIncludeUrl;
        CaptureExpandedTreeNodes();
        _uiPreferencesService.Save(UiPreferences);
    }

    private void CaptureExpandedTreeNodes()
    {
        var ids = new List<Guid>();
        foreach (var collection in Collections)
        {
            CollectExpandedNodeIds(collection, ids);
        }

        UiPreferences.ExpandedTreeNodeIds = ids;
    }

    private static void CollectExpandedNodeIds(TreeNodeViewModel node, List<Guid> ids)
    {
        if (node.IsExpanded && TryGetNodeId(node, out var id))
        {
            ids.Add(id);
        }

        foreach (var child in node.Children)
        {
            CollectExpandedNodeIds(child, ids);
        }
    }

    private void RestoreTreeExpandedState()
    {
        var expanded = new HashSet<Guid>(UiPreferences.ExpandedTreeNodeIds ?? []);
        foreach (var collection in Collections)
        {
            ApplyExpandedState(collection, expanded);
        }
    }

    private static void ApplyExpandedState(TreeNodeViewModel node, HashSet<Guid> expanded)
    {
        if (node.Children.Count > 0 && TryGetNodeId(node, out var id))
        {
            node.IsExpanded = expanded.Contains(id);
        }
        else
        {
            node.IsExpanded = false;
        }

        foreach (var child in node.Children)
        {
            ApplyExpandedState(child, expanded);
        }
    }

    private static bool TryGetNodeId(TreeNodeViewModel node, out Guid id)
    {
        switch (node)
        {
            case CollectionNodeViewModel collection:
                id = collection.Model.Id;
                return true;
            case FolderNodeViewModel folder:
                id = folder.Model.Id;
                return true;
            case RequestNodeViewModel request:
                id = request.Model.Id;
                return true;
            default:
                id = default;
                return false;
        }
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

    /// <summary>
    /// Raised immediately before Save / Save All so the UI can commit in-progress edits.
    /// </summary>
    public event Action? PendingEditCommitRequested;

    public string StorageDirectory => _persistence.StorageDirectory;

    public string BackupsDirectory => _backups.BackupsDirectory;

    public bool HasCollections => Collections.Count > 0;

    public string AppTitle => "RequestExplorer";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTreeFilter))]
    [NotifyPropertyChangedFor(nameof(TreeFilterPlaceholder))]
    [NotifyCanExecuteChangedFor(nameof(ClearTreeFilterCommand))]
    private string _treeFilterText = string.Empty;

    public bool HasTreeFilter => !string.IsNullOrWhiteSpace(TreeFilterText);

    private bool _suppressTreeSearchScopeGuard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeFilterPlaceholder))]
    private bool _treeFilterIncludeAncestors = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeFilterPlaceholder))]
    private bool _treeFilterIncludeName = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeFilterPlaceholder))]
    private bool _treeFilterIncludeUrl = true;

    public string TreeFilterPlaceholder
    {
        get
        {
            var parts = new List<string>();
            if (TreeFilterIncludeAncestors)
            {
                parts.Add("path");
            }

            if (TreeFilterIncludeName)
            {
                parts.Add("name");
            }

            if (TreeFilterIncludeUrl)
            {
                parts.Add("URL");
            }

            return parts.Count == 0
                ? "Filter…"
                : $"Filter by {string.Join(", ", parts)}…";
        }
    }

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
    [NotifyCanExecuteChangedFor(nameof(RunAllRequestsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CollapseChildrenCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExpandChildrenCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveNodeUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveNodeDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveToNewSubFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditAsJsonCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportIntoNodeCommand))]
    private TreeNodeViewModel? _selectedNode;

    private TreeClipboardPayload? _treeClipboard;

    private readonly List<TreeNodeViewModel> _selectedNodes = [];
    private TreeNodeViewModel? _selectionAnchor;
    private bool _suppressMoveSideEffects;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendRequestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSendCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAllRequestsCommand))]
    private bool _isSending;

    /// <summary>0=Params, 1=Body, 2=Headers, 3=Auth</summary>
    [ObservableProperty]
    private int _selectedRequestTabIndex;

    [ObservableProperty]
    private bool _isRootDropIndicatorVisible;

    [ObservableProperty]
    private bool _isQuickExportVisible;

    [ObservableProperty]
    private bool _isDarkMode;

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

    public IReadOnlyList<TreeNodeViewModel> SelectedNodes => _selectedNodes;

    public string SelectionSummary
    {
        get
        {
            if (SelectedNode is null)
            {
                return "Nothing selected";
            }

            if (_selectedNodes.Count > 1)
            {
                return $"{_selectedNodes.Count} items selected";
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

    public string RunAllRequestsMenuHeader => FormatRunAllRequestsMenuHeader(SelectedNode, includeMnemonic: true);

    public bool HasUndoHistory => _history.CanUndo;

    public bool HasRedoHistory => _history.CanRedo;

    public string FormatRunAllRequestsMenuHeader(TreeNodeViewModel? root, bool includeMnemonic = false)
    {
        var label = includeMnemonic ? "Run _All Requests" : "Run All Requests";
        if (root is null)
        {
            return label;
        }

        var count = EnumerateRequestsInHierarchy(root).Count();
        return count > 0 ? $"{label} ({count})" : label;
    }

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
            if (_selectedNodes.Count == 0 || !_selectedNodes.Contains(newValue))
            {
                ReplaceSelection([newValue], newValue);
            }
            else
            {
                SyncSelectionFlags();
            }
        }
        else if (_selectedNodes.Count > 0)
        {
            ClearSelectionSet();
        }

        if (newValue is RequestNodeViewModel newRequest)
        {
            AttachRequestExportWatchers(newRequest);
        }

        if (newValue is ContainerNodeViewModel container)
        {
            container.RefreshInheritedVariables();
        }

        CopyNodeCommand.NotifyCanExecuteChanged();
        PasteIntoNodeCommand.NotifyCanExecuteChanged();
        PasteAtRootCommand.NotifyCanExecuteChanged();
        SmartPasteCommand.NotifyCanExecuteChanged();
        EditAsJsonCommand.NotifyCanExecuteChanged();
        NotifyMoveAmongSiblingsCanExecuteChanged();
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(RunAllRequestsMenuHeader));
        RefreshQuickExport();
    }

    public bool IsNodeInSelection(TreeNodeViewModel node) => _selectedNodes.Contains(node);

    /// <summary>Raised after undo/redo restores tree nodes that should be scrolled into view.</summary>
    public event Action<IReadOnlyList<TreeNodeViewModel>>? TreeRevealRequested;

    public void SelectOnly(TreeNodeViewModel node)
    {
        ReplaceSelection([node], node);
        if (!ReferenceEquals(SelectedNode, node))
        {
            SelectedNode = node;
        }
        else
        {
            SyncSelectionFlags();
            OnPropertyChanged(nameof(SelectionSummary));
        }

        _selectionAnchor = node;
    }

    public void ToggleSelection(TreeNodeViewModel node)
    {
        if (_selectedNodes.Contains(node))
        {
            if (_selectedNodes.Count == 1)
            {
                return;
            }

            _selectedNodes.Remove(node);
            node.IsInSelection = false;
            if (ReferenceEquals(SelectedNode, node))
            {
                SelectedNode = _selectedNodes[^1];
            }
            else
            {
                SyncSelectionFlags();
                OnPropertyChanged(nameof(SelectionSummary));
                NotifyMoveAmongSiblingsCanExecuteChanged();
                EditAsJsonCommand.NotifyCanExecuteChanged();
            }
        }
        else
        {
            _selectedNodes.Add(node);
            node.IsInSelection = true;

            if (!ReferenceEquals(SelectedNode, node))
            {
                SelectedNode = node;
            }
            else
            {
                SyncSelectionFlags();
                OnPropertyChanged(nameof(SelectionSummary));
                NotifyMoveAmongSiblingsCanExecuteChanged();
                EditAsJsonCommand.NotifyCanExecuteChanged();
            }
        }

        _selectionAnchor = node;
    }

    public void SelectRange(TreeNodeViewModel toNode)
    {
        var anchor = _selectionAnchor ?? SelectedNode ?? toNode;
        if (!SharesSelectionParent(anchor, toNode))
        {
            SelectOnly(toNode);
            return;
        }

        var range = EnumerateSiblingRange(anchor, toNode)
            .Where(n => n.IsVisibleInFilter)
            .ToList();
        if (range.Count == 0)
        {
            SelectOnly(toNode);
            return;
        }

        ReplaceSelection(range, toNode);
        if (!ReferenceEquals(SelectedNode, toNode))
        {
            SelectedNode = toNode;
        }
        else
        {
            SyncSelectionFlags();
            OnPropertyChanged(nameof(SelectionSummary));
            NotifyMoveAmongSiblingsCanExecuteChanged();
            EditAsJsonCommand.NotifyCanExecuteChanged();
        }
    }

    public IReadOnlyList<TreeNodeViewModel> GetMoveBatch(TreeNodeViewModel? dragSource = null)
    {
        IReadOnlyList<TreeNodeViewModel> batch;
        if (dragSource is not null && _selectedNodes.Contains(dragSource) && _selectedNodes.Count > 1)
        {
            batch = _selectedNodes;
        }
        else if (dragSource is not null)
        {
            batch = [dragSource];
        }
        else if (_selectedNodes.Count > 0)
        {
            batch = _selectedNodes;
        }
        else
        {
            batch = SelectedNode is null ? [] : [SelectedNode];
        }

        // If a folder/collection and one of its descendants are both selected, only move the ancestor.
        return PruneRedundantDescendants(batch);
    }

    private static List<TreeNodeViewModel> PruneRedundantDescendants(IReadOnlyList<TreeNodeViewModel> batch)
    {
        if (batch.Count <= 1)
        {
            return batch.ToList();
        }

        return batch
            .Where(node => !batch.Any(other =>
                !ReferenceEquals(other, node) && IsUnder(node, other)))
            .ToList();
    }

    private void ReplaceSelection(IReadOnlyList<TreeNodeViewModel> nodes, TreeNodeViewModel primary)
    {
        foreach (var node in _selectedNodes)
        {
            node.IsInSelection = false;
        }

        _selectedNodes.Clear();
        foreach (var node in nodes.Distinct())
        {
            _selectedNodes.Add(node);
            node.IsInSelection = true;
        }

        if (!_selectedNodes.Contains(primary) && _selectedNodes.Count > 0)
        {
            primary = _selectedNodes[^1];
        }

        primary.IsInSelection = true;
        SyncSelectionFlags();
    }

    private void ClearSelectionSet()
    {
        foreach (var node in _selectedNodes)
        {
            node.IsInSelection = false;
        }

        _selectedNodes.Clear();
        _selectionAnchor = null;
    }

    private void SyncSelectionFlags()
    {
        foreach (var node in _selectedNodes)
        {
            node.IsInSelection = true;
        }
    }

    private static bool SharesSelectionParent(TreeNodeViewModel a, TreeNodeViewModel b)
    {
        if (a is CollectionNodeViewModel && a.Parent is null
            && b is CollectionNodeViewModel && b.Parent is null)
        {
            return true;
        }

        return a.Parent is not null && ReferenceEquals(a.Parent, b.Parent);
    }

    private IEnumerable<TreeNodeViewModel> EnumerateSiblingRange(TreeNodeViewModel a, TreeNodeViewModel b)
    {
        if (a is CollectionNodeViewModel && a.Parent is null
            && b is CollectionNodeViewModel && b.Parent is null)
        {
            var i = Collections.IndexOf((CollectionNodeViewModel)a);
            var j = Collections.IndexOf((CollectionNodeViewModel)b);
            if (i < 0 || j < 0)
            {
                yield break;
            }

            var start = Math.Min(i, j);
            var end = Math.Max(i, j);
            for (var k = start; k <= end; k++)
            {
                yield return Collections[k];
            }

            yield break;
        }

        var parent = a.Parent;
        if (parent is null || !ReferenceEquals(parent, b.Parent))
        {
            yield break;
        }

        var ai = parent.Children.IndexOf(a);
        var bi = parent.Children.IndexOf(b);
        if (ai < 0 || bi < 0)
        {
            yield break;
        }

        var from = Math.Min(ai, bi);
        var to = Math.Max(ai, bi);
        for (var k = from; k <= to; k++)
        {
            yield return parent.Children[k];
        }
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

        StatusMessage = value ? "Quick Export shown" : "Quick Export hidden";
        PersistUiPreferences();
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        ThemeService.Apply(value);
        StatusMessage = value ? "Dark mode on" : "Dark mode off";
        PersistUiPreferences();
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
    }

    [RelayCommand]
    private void HideQuickExport()
    {
        IsQuickExportVisible = false;
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

    bool IUndoRecorder.IsApplyingHistory => _isApplyingHistory || _suppressUndoRecording;

    void IUndoRecorder.Record(IUndoAction action) => RecordUndo(action);

    private void RecordUndo(IUndoAction undoInstruction)
    {
        if (_isApplyingHistory || _suppressUndoRecording)
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
            EditorSurface.Body => 0,
            EditorSurface.Params => 1,
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
        StatusMessage = SyncCollectionFileName(node, newName)
            ? $"Renamed to '{newName}' (file updated on disk)"
            : $"Renamed to '{newName}' (Ctrl+S to persist)";
    }

    void IUndoContext.SetName(TreeNodeViewModel node, string name)
    {
        SetName(node, name);
        SyncCollectionFileName(node, name);
    }

    /// <summary>
    /// When a collection already has a disk file, rename/move that file to match the tree name.
    /// </summary>
    private bool SyncCollectionFileName(TreeNodeViewModel node, string newName)
    {
        if (node is not CollectionNodeViewModel collection)
        {
            return false;
        }

        try
        {
            return _persistence.SyncFileNameToCollectionName(collection.Model.Id, newName);
        }
        catch (Exception ex)
        {
            if (!_isApplyingHistory)
            {
                MessageBox.Show(
                    $"The collection was renamed in the tree, but the on-disk file could not be renamed.\n\n{ex.Message}",
                    "Rename Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }
    }

    void IUndoContext.RemoveNode(TreeNodeViewModel node) => RemoveNodeCore(node);

    void IUndoContext.InsertNode(TreeNodeViewModel? parent, TreeNodeViewModel node, int index) =>
        InsertNodeCore(parent, node, index);

    void IUndoContext.Select(TreeNodeViewModel? node) => SelectedNode = node;

    void IUndoContext.RevealAndSelect(IReadOnlyList<TreeNodeViewModel> nodes) =>
        RevealAndSelectNodes(nodes);

    private void RevealAndSelectNodes(IReadOnlyList<TreeNodeViewModel> nodes)
    {
        var batch = nodes.Where(n => n is not null).Distinct().ToList();
        if (batch.Count == 0)
        {
            return;
        }

        foreach (var node in batch)
        {
            for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                ancestor.IsExpanded = true;
            }
        }

        // Refresh filter flags after structural undo; clear filter if it would hide restored nodes.
        ApplyTreeFilter();
        if (HasTreeFilter && batch.Any(n => !n.IsVisibleInFilter))
        {
            TreeFilterText = string.Empty;
        }

        var primary = batch[0];
        ReplaceSelection(batch, primary);
        _selectionAnchor = primary;
        if (!ReferenceEquals(SelectedNode, primary))
        {
            SelectedNode = primary;
        }
        else
        {
            SyncSelectionFlags();
            OnPropertyChanged(nameof(SelectionSummary));
        }

        TreeRevealRequested?.Invoke(batch);
    }

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

    private bool _suppressCollectionOrderPersist;

    private void LoadWorkspace(IReadOnlyList<Guid>? priorOrder = null)
    {
        _suppressCollectionOrderPersist = true;
        try
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

                var preferredOrder = priorOrder ?? _persistence.LoadCollectionOrder();
                var ordered = CollectionPersistenceService.ApplyCollectionOrder(loaded, preferredOrder);

                foreach (var definition in ordered)
                {
                    Collections.Add(CollectionTreeMapper.ToViewModel(definition));
                }

                WireUndoRecorders();
                RestoreTreeExpandedState();
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
                RestoreTreeExpandedState();
                StatusMessage = "Load failed — using in-memory demo (not saved)";
            }
        }
        finally
        {
            _suppressCollectionOrderPersist = false;
        }

        PersistCollectionOrder();
    }

    private void PersistCollectionOrder()
    {
        if (_suppressCollectionOrderPersist)
        {
            return;
        }

        try
        {
            _persistence.SaveCollectionOrder(Collections.Select(c => c.Model.Id));
        }
        catch
        {
            // Order persistence should not block workspace edits.
        }
    }

    [RelayCommand]
    private void CollapseAllNodes()
    {
        CollapseAllCollections();
        StatusMessage = "Collapsed all nodes";
    }

    private void CollapseAllCollections()
    {
        foreach (var collection in Collections)
        {
            CollapseSubtree(collection);
        }
    }

    partial void OnTreeFilterTextChanged(string value) => ScheduleDebouncedTreeFilter();

    partial void OnTreeFilterIncludeAncestorsChanged(bool value) => OnTreeSearchScopeFlagChanged();

    partial void OnTreeFilterIncludeNameChanged(bool value) => OnTreeSearchScopeFlagChanged();

    partial void OnTreeFilterIncludeUrlChanged(bool value) => OnTreeSearchScopeFlagChanged();

    private void OnTreeSearchScopeFlagChanged()
    {
        if (_suppressTreeSearchScopeGuard)
        {
            return;
        }

        if (!TreeFilterIncludeAncestors && !TreeFilterIncludeName && !TreeFilterIncludeUrl)
        {
            // Keep at least one component enabled.
            _suppressTreeSearchScopeGuard = true;
            TreeFilterIncludeName = true;
            _suppressTreeSearchScopeGuard = false;
        }

        SyncTreeSearchScope();
        PersistUiPreferences();
        ApplyTreeFilter();
        NotifyAllSearchTextsChanged();
    }

    private void EnsureValidTreeSearchScope()
    {
        if (TreeFilterIncludeAncestors || TreeFilterIncludeName || TreeFilterIncludeUrl)
        {
            return;
        }

        _suppressTreeSearchScopeGuard = true;
        TreeFilterIncludeName = true;
        _suppressTreeSearchScopeGuard = false;
    }

    private void SyncTreeSearchScope() =>
        TreeNodeViewModel.SearchScope = new TreeSearchScope(
            TreeFilterIncludeAncestors,
            TreeFilterIncludeName,
            TreeFilterIncludeUrl);

    private void NotifyAllSearchTextsChanged()
    {
        foreach (var collection in Collections)
        {
            collection.NotifySearchTextChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanClearTreeFilter))]
    private void ClearTreeFilter() => TreeFilterText = string.Empty;

    private bool CanClearTreeFilter() => HasTreeFilter;

    private bool _treeFilterApplyQueued;
    private bool _deferTreeFilterDuringAltMove;
    private DispatcherTimer? _treeFilterDebounceTimer;
    private const int TreeFilterDebounceMilliseconds = 200;

    private void ApplyTreeFilterIfActive()
    {
        if (_deferTreeFilterDuringAltMove || !HasTreeFilter || _treeFilterApplyQueued)
        {
            return;
        }

        _treeFilterApplyQueued = true;
        Application.Current?.Dispatcher.BeginInvoke(
            () =>
            {
                _treeFilterApplyQueued = false;
                if (_deferTreeFilterDuringAltMove || !HasTreeFilter)
                {
                    return;
                }

                ApplyTreeFilter();
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Called when Alt is released after keyboard sibling moves so filter visibility
    /// can catch up without flickering mid-move.
    /// </summary>
    public void EndAltMoveFilterDeferral()
    {
        if (!_deferTreeFilterDuringAltMove)
        {
            return;
        }

        _deferTreeFilterDuringAltMove = false;
        if (HasTreeFilter)
        {
            ApplyTreeFilter();
        }
    }

    /// <summary>
    /// Waits until typing pauses before walking the tree. Clearing the box applies immediately.
    /// </summary>
    private void ScheduleDebouncedTreeFilter()
    {
        if (string.IsNullOrWhiteSpace(TreeFilterText))
        {
            ApplyTreeFilter();
            return;
        }

        if (_treeFilterDebounceTimer is null)
        {
            _treeFilterDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(TreeFilterDebounceMilliseconds)
            };
            _treeFilterDebounceTimer.Tick += (_, _) =>
            {
                _treeFilterDebounceTimer.Stop();
                ApplyTreeFilter();
            };
        }

        _treeFilterDebounceTimer.Stop();
        _treeFilterDebounceTimer.Start();
    }

    private void ApplyTreeFilter()
    {
        _treeFilterDebounceTimer?.Stop();

        var filter = TreeFilterText.Trim();
        var filtering = filter.Length > 0;

        foreach (var collection in Collections)
        {
            ApplyTreeFilterRecursive(collection, filter, filtering);
        }

        ClearTreeFilterCommand.NotifyCanExecuteChanged();
    }

    private static bool ApplyTreeFilterRecursive(TreeNodeViewModel node, string filter, bool filtering)
    {
        var selfMatch = !filtering
                        || node.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var descendantMatch = false;
        foreach (var child in node.Children)
        {
            if (ApplyTreeFilterRecursive(child, filter, filtering))
            {
                descendantMatch = true;
            }
        }

        var visible = selfMatch || descendantMatch;
        node.IsVisibleInFilter = visible;

        // Reveal matches under collapsed parents while filtering.
        if (filtering && descendantMatch)
        {
            node.IsExpanded = true;
        }

        return visible;
    }

    private void SeedAndPersistDemo()
    {
        foreach (var definition in SampleDataFactory.CreateDemoDefinitions())
        {
            _persistence.Save(definition);
            Collections.Add(CollectionTreeMapper.ToViewModel(definition));
        }
    }

    /// <summary>
    /// Adds the same demo collections created on first launch (httpbin.org Demo + Sandbox).
    /// </summary>
    [RelayCommand]
    private void RecreateDemoCollections()
    {
        var definitions = SampleDataFactory.CreateDemoDefinitions();
        if (definitions.Count == 0)
        {
            return;
        }

        var previousSelection = _selectedNodes.Count > 0
            ? _selectedNodes.ToList()
            : SelectedNode is null ? [] : new List<TreeNodeViewModel> { SelectedNode };

        var added = new List<CollectionNodeViewModel>();
        _suppressUndoRecording = true;
        _suppressCollectionOrderPersist = true;
        try
        {
            foreach (var definition in definitions)
            {
                var node = CollectionTreeMapper.ToViewModel(definition);
                WireUndoRecorders(node);
                Collections.Add(node);
                CollapseSubtree(node);
                node.IsExpanded = true;
                MarkSubtreeDirty(node);
                added.Add(node);
            }

            ReplaceSelection(added, added[^1]);
            SelectedNode = added[^1];
        }
        finally
        {
            _suppressCollectionOrderPersist = false;
            _suppressUndoRecording = false;
            PersistCollectionOrder();
        }

        var undoSteps = added
            .AsEnumerable()
            .Reverse()
            .Select(n => CreateRemoveAction(n, $"Add demo '{n.Name}'"))
            .ToList();
        RecordUndo(new CompositeUndoAction(
            added.Count == 1
                ? $"Add demo collection '{added[0].Name}'"
                : $"Add {added.Count} demo collections",
            undoSteps,
            selectAfter: added,
            selectAfterInverse: previousSelection));

        OnPropertyChanged(nameof(HasAnyDirty));
        StatusMessage = added.Count == 1
            ? $"Added demo collection '{added[0].Name}' (Ctrl+S to persist)"
            : $"Added {added.Count} demo collections (Ctrl+S to persist)";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        PendingEditCommitRequested?.Invoke();

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
        PendingEditCommitRequested?.Invoke();

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
        // Normalize spare rows before persist so a blank mid-list cannot upset sync/save.
        NormalizeKeyValueSpares(collection);
        CollectionTreeMapper.SyncModelFromView(collection);
        _persistence.Save(collection.Model);
        CollectionTreeMapper.ClearDirty(collection);
        OnPropertyChanged(nameof(HasAnyDirty));
    }

    private static void NormalizeKeyValueSpares(TreeNodeViewModel node)
    {
        switch (node)
        {
            case RequestNodeViewModel request:
                request.NormalizeSpareRows();
                break;
            case ContainerNodeViewModel container:
                container.NormalizeSpareRows();
                break;
        }

        foreach (var child in node.Children)
        {
            NormalizeKeyValueSpares(child);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSendRequest))]
    private async Task SendRequestAsync()
    {
        if (SelectedRequest is null || IsSending)
        {
            return;
        }

        // Capture the request so a selection change mid-flight still updates the right slot.
        var request = SelectedRequest;
        var response = request.LastResponse;

        var diagnosis = VariableDiagnostics.DiagnoseRequest(request);
        if (diagnosis.HasIssues)
        {
            var message = VariableDiagnostics.FormatRequestReport(diagnosis);
            response.Apply(new HttpExecutionResult
            {
                Succeeded = false,
                ErrorMessage = message
            });
            StatusMessage = message;
            return;
        }

        _sendCts?.Cancel();
        _sendCts?.Dispose();
        _sendCts = new CancellationTokenSource();

        IsSending = true;
        response.BeginSend();
        var resolved = VariableResolver.Resolve(request.Model, request);
        StatusMessage = $"Sending {request.Verb} {resolved.Address}…";

        try
        {
            var result = await _httpExecutor.SendAsync(resolved, _sendCts.Token);
            response.Apply(result);

            StatusMessage = result.Succeeded
                ? $"Response {result.StatusCode} in {response.ElapsedDisplay}"
                : $"Send failed: {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            response.Apply(new HttpExecutionResult
            {
                Succeeded = false,
                ErrorMessage = ex.Message
            });
            StatusMessage = $"Send failed: {ex.Message}";
        }
        finally
        {
            IsSending = false;
            response.IsSending = false;
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
        var report = VariableDiagnostics.BuildSubtreeReport(SelectedNode, results);
        var failing = results.Count(r => r.HasIssues);
        StatusMessage = failing == 0
            ? $"Diagnosis OK — {results.Count} request(s)"
            : $"Diagnosis: {failing} of {results.Count} request(s) have missing variables";

        var dialog = new Views.DiagnosisReportDialog("Variable Diagnosis", report)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();

        if (dialog.NavigatedNode is { } target)
        {
            RevealAndSelectNodes([target]);
            StatusMessage = $"Selected {target.HierarchyPath}";
        }
    }

    private bool CanDiagnoseVariables() => SelectedNode is not null;

    [RelayCommand(CanExecute = nameof(CanRunAllRequests))]
    private async Task RunAllRequestsAsync(TreeNodeViewModel? root = null)
    {
        root ??= SelectedNode;
        if (root is null || IsSending)
        {
            return;
        }

        var requests = EnumerateRequestsInHierarchy(root).ToList();
        if (requests.Count == 0)
        {
            StatusMessage = "No requests in selection";
            return;
        }

        PendingEditCommitRequested?.Invoke();

        var rows = new ObservableCollection<Views.RunAllRequestResultRow>(
            requests.Select((r, index) => new Views.RunAllRequestResultRow
            {
                Request = r,
                TreeOrder = index,
                Name = r.Name,
                HierarchyDisplay = Views.RunAllRequestResultRow.BuildHierarchyDisplay(r)
            }));

        Views.RunAllResultsDialog? dialog = null;

        void BeginSendingState()
        {
            IsSending = true;
            SendRequestCommand.NotifyCanExecuteChanged();
            CancelSendCommand.NotifyCanExecuteChanged();
            RunAllRequestsCommand.NotifyCanExecuteChanged();
        }

        void EndSendingState()
        {
            IsSending = false;
            SendRequestCommand.NotifyCanExecuteChanged();
            CancelSendCommand.NotifyCanExecuteChanged();
            RunAllRequestsCommand.NotifyCanExecuteChanged();
        }

        void PublishSummary(string summary)
        {
            StatusMessage = summary;
            try
            {
                if (dialog is { IsLoaded: true })
                {
                    dialog.SetSummary(summary);
                    dialog.MarkFinished();
                }
            }
            catch (InvalidOperationException)
            {
                // Dialog may already be closing/closed after cancel-via-close.
            }
        }

        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        Task OnUiAsync(Action action)
        {
            if (dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(action).Task;
        }

        /// <summary>Returns true when the request was cancelled mid-send.</summary>
        async Task<bool> ExecuteOneAsync(Views.RunAllRequestResultRow row, CancellationToken token)
        {
            var request = row.Request;
            var response = request.LastResponse;

            await OnUiAsync(() =>
            {
                row.StatusDisplay = "Running…";
                row.ClearTiming();
                row.ApplyExpectationResult(null);
                response.BeginSend();
            });

            try
            {
                var diagnosis = VariableDiagnostics.DiagnoseRequest(request);
                if (diagnosis.HasIssues)
                {
                    var message = VariableDiagnostics.FormatRequestReport(diagnosis);
                    await OnUiAsync(() =>
                    {
                        response.Apply(new HttpExecutionResult
                        {
                            Succeeded = false,
                            ErrorMessage = message
                        });
                        row.StatusDisplay = message;
                        row.ClearTiming();
                        row.Succeeded = false;
                        row.ApplyExpectationResult(ExpectationCheckResult.UnmetWithoutResponse);
                    });
                    return false;
                }

                var resolved = VariableResolver.Resolve(request.Model, request);
                var result = await _httpExecutor.SendAsync(resolved, token);

                if (token.IsCancellationRequested)
                {
                    await OnUiAsync(() =>
                    {
                        response.Apply(new HttpExecutionResult
                        {
                            Succeeded = false,
                            ErrorMessage = "Cancelled"
                        });
                        row.StatusDisplay = "Cancelled";
                        row.ClearTiming();
                        row.Succeeded = false;
                        row.ApplyExpectationResult(ExpectationCheckResult.UnmetWithoutResponse);
                    });
                    return true;
                }

                var checkTiming = dialog!.CheckTiming;
                await OnUiAsync(() =>
                {
                    response.Apply(result);
                    row.StatusDisplay = result.Succeeded
                        ? $"{result.StatusCode} {result.ReasonPhrase}".Trim()
                        : (result.ErrorMessage ?? "Failed");
                    row.SetTiming(result.Timing.TotalMilliseconds, response.ElapsedDisplay);
                    row.Succeeded = result.Succeeded;
                    row.ApplyExpectationResult(
                        RequestExpectations.Evaluate(request, result, checkTiming));
                });
                return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                await OnUiAsync(() =>
                {
                    response.Apply(new HttpExecutionResult
                    {
                        Succeeded = false,
                        ErrorMessage = "Cancelled"
                    });
                    row.StatusDisplay = "Cancelled";
                    row.ClearTiming();
                    row.Succeeded = false;
                    row.ApplyExpectationResult(ExpectationCheckResult.UnmetWithoutResponse);
                });
                return true;
            }
            catch (Exception ex)
            {
                await OnUiAsync(() =>
                {
                    response.Apply(new HttpExecutionResult
                    {
                        Succeeded = false,
                        ErrorMessage = ex.Message
                    });
                    row.StatusDisplay = ex.Message;
                    row.ClearTiming();
                    row.Succeeded = false;
                    row.ApplyExpectationResult(ExpectationCheckResult.UnmetWithoutResponse);
                });
                return false;
            }
            finally
            {
                await OnUiAsync(() => response.IsSending = false);
            }
        }

        string FormatPassSummary(bool cancelled, int completed, int notCompleted) =>
            cancelled
                ? $"Cancelled — {completed} completed, {notCompleted} not completed"
                : $"Finished — {completed} completed, {notCompleted} not completed";

        static (int completed, int notCompleted) TallyRows(IEnumerable<Views.RunAllRequestResultRow> source)
        {
            var completed = 0;
            var notCompleted = 0;
            foreach (var row in source)
            {
                // Skipped / cancelled / still queued never finished a send.
                if (row.StatusDisplay is "Pending" or "Skipped" or "Running…" or "Cancelled")
                {
                    notCompleted++;
                }
                else
                {
                    // Any terminal status (incl. 4xx/5xx or transport errors) counts as completed.
                    completed++;
                }
            }

            return (completed, notCompleted);
        }

        void MarkIncompleteRowsSkipped()
        {
            foreach (var row in rows)
            {
                if (row.StatusDisplay is "Pending" or "Running…")
                {
                    row.StatusDisplay = row.StatusDisplay == "Running…" ? "Cancelled" : "Skipped";
                    if (row.StatusDisplay == "Cancelled")
                    {
                        row.ClearTiming();
                        row.Succeeded = false;
                        row.ApplyExpectationResult(ExpectationCheckResult.UnmetWithoutResponse);
                    }
                    else
                    {
                        row.ApplyExpectationResult(null);
                    }
                }
            }
        }

        async Task ExecutePassAsync()
        {
            _sendCts?.Cancel();
            _sendCts?.Dispose();
            _sendCts = new CancellationTokenSource();
            var token = _sendCts.Token;
            var maxThreads = dialog!.MaxThreads;

            BeginSendingState();
            dialog.MarkRunning();
            foreach (var row in rows)
            {
                row.StatusDisplay = "Pending";
                row.ClearTiming();
                row.Succeeded = false;
                row.ApplyExpectationResult(null);
            }

            var progressCompleted = 0;
            var cancelled = false;
            using var gate = new SemaphoreSlim(maxThreads, maxThreads);

            try
            {
                var progress = maxThreads <= 1
                    ? $"Running {requests.Count} request(s)…"
                    : $"Running {requests.Count} request(s) with {maxThreads} threads…";
                StatusMessage = progress;
                dialog.SetSummary(progress);

                var tasks = rows.Select(async row =>
                {
                    try
                    {
                        await gate.WaitAsync(token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        await OnUiAsync(() =>
                        {
                            if (row.StatusDisplay == "Pending")
                            {
                                row.StatusDisplay = "Skipped";
                                row.ApplyExpectationResult(null);
                            }
                        });
                        return;
                    }

                    try
                    {
                        if (token.IsCancellationRequested)
                        {
                            await OnUiAsync(() =>
                            {
                                if (row.StatusDisplay == "Pending")
                                {
                                    row.StatusDisplay = "Skipped";
                                    row.ApplyExpectationResult(null);
                                }
                            });
                            return;
                        }

                        var wasCancelled = await ExecuteOneAsync(row, token);
                        await OnUiAsync(() =>
                        {
                            if (wasCancelled)
                            {
                                cancelled = true;
                            }

                            progressCompleted++;
                            var summary = cancelled
                                ? $"Cancelling… {progressCompleted}/{requests.Count}"
                                : maxThreads <= 1
                                    ? $"Running {progressCompleted}/{requests.Count}: {row.Name}…"
                                    : $"Running {progressCompleted}/{requests.Count} ({maxThreads} threads)…";
                            StatusMessage = summary;
                            dialog.SetSummary(summary);
                        });
                    }
                    finally
                    {
                        gate.Release();
                    }
                });

                await Task.WhenAll(tasks);

                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    await OnUiAsync(MarkIncompleteRowsSkipped);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancelled = true;
                await OnUiAsync(MarkIncompleteRowsSkipped);
            }
            finally
            {
                EndSendingState();
                var (completed, notCompleted) = TallyRows(rows);
                PublishSummary(FormatPassSummary(cancelled, completed, notCompleted));
            }
        }

        async Task ExecuteRowAsync(Views.RunAllRequestResultRow row)
        {
            _sendCts?.Cancel();
            _sendCts?.Dispose();
            _sendCts = new CancellationTokenSource();
            var token = _sendCts.Token;

            BeginSendingState();
            dialog!.MarkRunning();

            var progress = $"Rerunning: {row.Name}…";
            StatusMessage = progress;
            dialog.SetSummary(progress);

            try
            {
                await ExecuteOneAsync(row, token);
            }
            finally
            {
                EndSendingState();
                var (completed, notCompleted) = TallyRows(rows);
                var summary =
                    $"Reran {row.Name} — {completed} completed, {notCompleted} not completed";
                PublishSummary(summary);
            }
        }

        dialog = new Views.RunAllResultsDialog(
            $"Run All — {root.Name}",
            rows,
            () => _sendCts?.Cancel(),
            ExecutePassAsync,
            ExecuteRowAsync,
            initialMaxThreads: UiPreferences.RunAllMaxThreads,
            initialCheckTiming: UiPreferences.RunAllCheckTiming,
            initialWidth: UiPreferences.RunAllWindowWidth,
            initialHeight: UiPreferences.RunAllWindowHeight,
            initialLeft: UiPreferences.RunAllWindowLeft,
            initialTop: UiPreferences.RunAllWindowTop)
        {
            Owner = Application.Current?.MainWindow
        };

        var runStarted = false;
        dialog.Loaded += async (_, _) =>
        {
            if (runStarted)
            {
                return;
            }

            runStarted = true;
            await ExecutePassAsync();
        };

        dialog.ShowDialog();

        UiPreferences.RunAllMaxThreads = dialog.MaxThreads;
        UiPreferences.RunAllCheckTiming = dialog.CheckTiming;
        dialog.CaptureWindowBounds(out var width, out var height, out var left, out var top);
        UiPreferences.RunAllWindowWidth = width;
        UiPreferences.RunAllWindowHeight = height;
        UiPreferences.RunAllWindowLeft = left;
        UiPreferences.RunAllWindowTop = top;
        PersistUiPreferences();

        if (dialog.NavigatedNode is { } target)
        {
            RevealAndSelectNodes([target]);
            StatusMessage = $"Selected {target.HierarchyPath}";
        }
    }

    private bool CanRunAllRequests(TreeNodeViewModel? root = null)
    {
        if (IsSending)
        {
            return false;
        }

        root ??= SelectedNode;
        return root is not null && EnumerateRequestsInHierarchy(root).Any();
    }

    private static IEnumerable<RequestNodeViewModel> EnumerateRequestsInHierarchy(TreeNodeViewModel root)
    {
        if (root is RequestNodeViewModel request)
        {
            yield return request;
            yield break;
        }

        foreach (var child in root.Children)
        {
            foreach (var nested in EnumerateRequestsInHierarchy(child))
            {
                yield return nested;
            }
        }
    }

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

    [RelayCommand(CanExecute = nameof(CanEditAsJson))]
    private void EditAsJson()
    {
        if (!CanEditAsJson() || SelectedNode is null)
        {
            return;
        }

        var target = SelectedNode;
        PendingEditCommitRequested?.Invoke();
        NormalizeKeyValueSpares(target);
        PrepareNodeForExport(target);

        var json = SubtreeJsonService.Serialize(target);
        var dialog = new Views.EditAsJsonDialog(target.Kind, target.Name, json)
        {
            Owner = Application.Current?.MainWindow,
            TryApply = buffer => TryApplySubtreeJson(target, buffer)
        };
        dialog.ShowDialog();
    }

    private bool CanEditAsJson() =>
        _selectedNodes.Count == 1
        && SelectedNode is CollectionNodeViewModel or FolderNodeViewModel or RequestNodeViewModel;

    private (bool ok, string? error) TryApplySubtreeJson(TreeNodeViewModel target, string json)
    {
        var rootId = target switch
        {
            CollectionNodeViewModel collection => collection.Model.Id,
            FolderNodeViewModel folder => folder.Model.Id,
            RequestNodeViewModel request => request.Model.Id,
            _ => Guid.Empty
        };

        if (!SubtreeJsonService.TryDeserialize(target.Kind, json, rootId, out var model, out var error)
            || model is null)
        {
            return (false, error ?? "Invalid JSON.");
        }

        TreeNodeViewModel newNode;
        try
        {
            newNode = model switch
            {
                CollectionDefinition collection => CollectionTreeMapper.ToViewModel(collection),
                FolderDefinition folder => CollectionTreeMapper.ToFolderViewModel(folder),
                RequestDefinition request => new RequestNodeViewModel(request),
                _ => throw new InvalidOperationException("Unexpected model type.")
            };
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }

        WireUndoRecorders(newNode);

        var parent = target.Parent;
        var index = parent is not null
            ? parent.Children.IndexOf(target)
            : Collections.IndexOf((CollectionNodeViewModel)target);
        if (index < 0)
        {
            return (false, "Could not locate the selected node in the tree.");
        }

        var description = $"Edit as JSON '{target.Name}'";

        // Avoid leaving selection on a node about to be removed.
        if (ReferenceEquals(SelectedNode, target) || IsUnder(SelectedNode, target))
        {
            SelectedNode = parent;
        }

        RemoveNodeCore(target);
        InsertNodeCore(parent, newNode, index);

        if (parent is not null)
        {
            parent.IsExpanded = true;
        }

        RecordUndo(new CompositeUndoAction(
            description,
            [
                new RemoveNodeAction(newNode, parent, index, description),
                new InsertNodeAction(parent, target, index, description)
            ],
            selectAfter: [newNode],
            selectAfterInverse: [target]));

        if (newNode is CollectionNodeViewModel appliedCollection)
        {
            // InsertNodeCore already persisted; keep dirty marker so the workspace reflects the edit.
            appliedCollection.IsDirty = true;
            try
            {
                _persistence.SyncFileNameToCollectionName(appliedCollection.Model.Id, appliedCollection.Name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"The collection was updated, but the on-disk file could not be renamed.\n\n{ex.Message}",
                    "Rename Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        else
        {
            MarkSubtreeDirty(newNode);
            var owning = CollectionTreeMapper.FindRootCollection(newNode);
            if (owning is not null)
            {
                owning.IsDirty = true;
            }
        }

        SelectOnly(newNode);
        OnPropertyChanged(nameof(HasAnyDirty));
        StatusMessage = newNode is CollectionNodeViewModel
            ? $"Applied JSON to '{newNode.Name}'"
            : $"Applied JSON to '{newNode.Name}' (Ctrl+S to persist)";
        return (true, null);
    }

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
    private void OpenBackupsFolder()
    {
        try
        {
            _backups.EnsureBackupsDirectory();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_backups.BackupsDirectory}\"",
                UseShellExecute = true
            });
            StatusMessage = "Opened backups folder";
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
    private void BackupNow()
    {
        if (HasAnyDirty)
        {
            MessageBox.Show(
                Application.Current?.MainWindow,
                "You have unsaved changes. Backup uses on-disk files only, so those edits will not be included until you save.",
                "Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        try
        {
            StatusMessage = "Backing up…";
            var path = RunBackup(BackupReason.Manual);
            StatusMessage = $"Backup created — {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Backup failed";
            MessageBox.Show(
                Application.Current?.MainWindow,
                ex.Message,
                "Backup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RestoreBackup()
    {
        if (!ConfirmSaveIfDirty("You have unsaved changes. Save all before restoring a backup?"))
        {
            return;
        }

        IReadOnlyList<BackupListItem> backups;
        try
        {
            backups = _backups.ListBackups();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Application.Current?.MainWindow,
                ex.Message,
                "Restore Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var picker = new Views.RestoreBackupDialog(backups)
        {
            Owner = Application.Current?.MainWindow
        };
        if (picker.ShowDialog() != true || picker.SelectedBackup is null)
        {
            return;
        }

        var selected = picker.SelectedBackup;
        var confirmed = Views.ConfirmDialog.Confirm(
            Application.Current?.MainWindow,
            "Restore backup",
            $"Replace all collection files on disk with backup '{selected.DisplayName}'?\n\n"
            + "Preferences and window layout will not change.\n"
            + "A safety backup of the current collections will be created first.\n\n"
            + "This cannot be undone except by restoring another backup.",
            yesText: "Restore",
            noText: "Cancel");
        if (!confirmed)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _backupBusy, 1, 0) != 0)
        {
            MessageBox.Show(
                Application.Current?.MainWindow,
                "A backup or restore is already in progress.",
                "Restore",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            StatusMessage = "Creating safety backup…";
            PersistUiPreferences();
            _backups.CreateBackup(
                BackupReason.PreRestore,
                WorkspaceBackupService.ClampRetention(UiPreferences.BackupRetentionCount));

            StatusMessage = "Restoring collections…";
            _backups.RestoreCollections(selected.ZipPath);
            ReloadWorkspaceFromDisk($"Restored from {selected.DisplayName}");
        }
        catch (Exception ex)
        {
            StatusMessage = "Restore failed";
            MessageBox.Show(
                Application.Current?.MainWindow,
                ex.Message,
                "Restore Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _backupBusy, 0);
        }
    }

    /// <summary>Called after the main window is shown — runs startup backup off the UI thread when enabled.</summary>
    public void StartStartupBackupIfNeeded()
    {
        if (!UiPreferences.BackupOnStartup)
        {
            return;
        }

        StatusMessage = "Backing up…";
        _ = Task.Run(() =>
        {
            try
            {
                var path = RunBackup(BackupReason.Startup);
                var name = Path.GetFileName(path);
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (StatusMessage.StartsWith("Backing up", StringComparison.OrdinalIgnoreCase)
                        || StatusMessage is "Ready"
                        || StatusMessage.Contains("collection", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusMessage = $"Startup backup created — {name}";
                    }
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    StatusMessage = "Startup backup failed";
                    MessageBox.Show(
                        Application.Current?.MainWindow,
                        ex.Message,
                        "Backup Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
        });
    }

    private string RunBackup(BackupReason reason)
    {
        if (Interlocked.CompareExchange(ref _backupBusy, 1, 0) != 0)
        {
            throw new InvalidOperationException("A backup or restore is already in progress.");
        }

        try
        {
            // Flush current prefs to disk so the zip includes latest settings (restore still skips prefs).
            PersistUiPreferences();
            return _backups.CreateBackup(
                reason,
                WorkspaceBackupService.ClampRetention(UiPreferences.BackupRetentionCount));
        }
        finally
        {
            Interlocked.Exchange(ref _backupBusy, 0);
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var dialog = new Views.SettingsDialog(UiPreferences, _backups.BackupsDirectory)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        UiPreferences.RequestTimeoutSeconds = dialog.RequestTimeoutSeconds;
        UiPreferences.DevModeIgnoreTimeout = dialog.DevModeIgnoreTimeout;
        UiPreferences.BackupOnStartup = dialog.BackupOnStartup;
        UiPreferences.BackupRetentionCount =
            WorkspaceBackupService.ClampRetention(dialog.BackupRetentionCount);
        ApplyHttpTimeoutSettings();
        PersistUiPreferences();

        StatusMessage = UiPreferences.DevModeIgnoreTimeout
            ? "Settings saved — timeout ignored (dev mode)"
            : $"Settings saved — timeout {UiPreferences.RequestTimeoutSeconds}s, keep {UiPreferences.BackupRetentionCount} backup(s)";
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
    private void ExportNode(TreeNodeViewModel? node) =>
        ExportNodeCore(node, flattenVariables: false);

    [RelayCommand]
    private void ExportNodeFlattened(TreeNodeViewModel? node) =>
        ExportNodeCore(node, flattenVariables: true);

    private void ExportNodeCore(TreeNodeViewModel? node, bool flattenVariables)
    {
        node ??= SelectedNode;
        if (node is null)
        {
            return;
        }

        try
        {
            PrepareNodeForExport(node);
            var document = flattenVariables
                ? PostmanCollectionMapper.ToFlattenedCollectionDocument(node)
                : node switch
                {
                    CollectionNodeViewModel collection => PostmanCollectionMapper.ToCollectionDocument(collection.Model),
                    FolderNodeViewModel folder => PostmanCollectionMapper.ToCollectionDocument(folder.Model),
                    RequestNodeViewModel request => PostmanCollectionMapper.ToCollectionDocument(request.Model),
                    _ => throw new InvalidOperationException("Unsupported node type.")
                };

            var safeName = string.Join("_", node.Name.Split(Path.GetInvalidFileNameChars()));
            var suffix = flattenVariables ? ".flattened.postman_collection.json" : ".postman_collection.json";
            var dialog = new SaveFileDialog
            {
                Title = flattenVariables ? "Export to Postman (Flatten)" : "Export to Postman",
                Filter = "Postman Collection (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"{safeName}{suffix}",
                DefaultExt = ".json",
                AddExtension = true
            };

            if (dialog.ShowDialog(Application.Current?.MainWindow) != true)
            {
                return;
            }

            var json = JsonSerializer.Serialize(document, PostmanCollectionMapper.JsonOptions);
            File.WriteAllText(dialog.FileName, json);
            StatusMessage = flattenVariables
                ? $"Exported '{node.Name}' for Postman (variables flattened)"
                : $"Exported '{node.Name}' for Postman";
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

    [RelayCommand(CanExecute = nameof(CanImportIntoNode))]
    private void ImportIntoNode(TreeNodeViewModel? node)
    {
        var target = node ?? SelectedNode;
        if (target is not CollectionNodeViewModel and not FolderNodeViewModel)
        {
            return;
        }

        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import Postman Collection(s)",
                Filter =
                    "Postman Collection (*.postman_collection*;*.json)|*.postman_collection;*.postman_collection.json;*.json|All files (*.*)|*.*",
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
                    // Into-node import always treats content as Postman (folder under target).
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

                    var child = AttachImportedFolder(target, importedFolder);
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

            target.IsExpanded = true;
            target.IsDirty = true;
            foreach (var child in importedNodes)
            {
                CollapseSubtree(child);
                MarkSubtreeDirty(child);
            }

            var undoSteps = importedNodes
                .AsEnumerable()
                .Reverse()
                .Select(n => CreateRemoveAction(n, $"Import into '{target.Name}'"))
                .ToList();
            RecordUndo(new CompositeUndoAction(
                importedNodes.Count == 1
                    ? $"Import '{importedNodes[0].Name}' into '{target.Name}'"
                    : $"Import {importedNodes.Count} collections into '{target.Name}'",
                undoSteps));

            SelectedNode = importedNodes[^1];
            OnPropertyChanged(nameof(HasAnyDirty));
            StatusMessage = importedNodes.Count == 1
                ? $"Imported '{importedNodes[0].Name}' into '{target.Name}' (Ctrl+S to persist)"
                : $"Imported {importedNodes.Count} collections into '{target.Name}' (Ctrl+S to persist)";

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

    private bool CanImportIntoNode(TreeNodeViewModel? node) =>
        (node ?? SelectedNode) is CollectionNodeViewModel or FolderNodeViewModel;

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

    private static readonly JsonSerializerOptions NativeCollectionJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Imports each collection file as a new top-level collection (one collection per file).
    /// Accepts Postman (<c>.postman_collection</c> / <c>.postman_collection.json</c>) and
    /// RequestExplorer / generic <c>.json</c> files; format is detected from the name and contents.
    /// </summary>
    public void ImportCollectionsFromFiles(IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var importFiles = filePaths
            .Where(PostmanCollectionMapper.IsImportableCollectionFile)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (importFiles.Count == 0)
        {
            return;
        }

        var importedNodes = new List<CollectionNodeViewModel>();
        var errors = new List<string>();
        var previousSelection = _selectedNodes.Count > 0
            ? _selectedNodes.ToList()
            : SelectedNode is null ? [] : new List<TreeNodeViewModel> { SelectedNode };

        // Suppress incidental undo from binding/selection while building and selecting imports.
        _suppressUndoRecording = true;
        _suppressCollectionOrderPersist = true;
        try
        {
            foreach (var filePath in importFiles)
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var model = ParseImportedCollectionFile(filePath, json);
                    // Always assign a fresh id so a re-import never clashes with an existing collection.
                    model.Id = Guid.NewGuid();

                    var fileHint = PostmanCollectionMapper.ResolveImportNameFromFilePath(filePath);
                    if (string.IsNullOrWhiteSpace(model.Name))
                    {
                        model.Name = string.IsNullOrWhiteSpace(fileHint) ? "Imported Collection" : fileHint;
                    }

                    if (model.Folders.Count == 0
                        && model.Requests.Count == 0
                        && model.Variables.Count == 0)
                    {
                        errors.Add($"{Path.GetFileName(filePath)}: no items or variables to import.");
                        continue;
                    }

                    var node = CollectionTreeMapper.ToViewModel(model);
                    WireUndoRecorders(node);
                    Collections.Add(node);
                    CollapseSubtree(node);
                    node.IsExpanded = true;
                    MarkSubtreeDirty(node);
                    importedNodes.Add(node);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            if (importedNodes.Count > 0)
            {
                ReplaceSelection(importedNodes, importedNodes[^1]);
                SelectedNode = importedNodes[^1];
            }
        }
        finally
        {
            _suppressCollectionOrderPersist = false;
            _suppressUndoRecording = false;
            PersistCollectionOrder();
        }

        if (importedNodes.Count == 0)
        {
                MessageBox.Show(
                    errors.Count > 0
                        ? string.Join(Environment.NewLine, errors)
                        : "No collections were imported.",
                    "Import",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            return;
        }

        var undoSteps = importedNodes
            .AsEnumerable()
            .Reverse()
            .Select(n => CreateRemoveAction(n, $"Import '{n.Name}'"))
            .ToList();
        RecordUndo(new CompositeUndoAction(
            importedNodes.Count == 1
                ? $"Import '{importedNodes[0].Name}'"
                : $"Import {importedNodes.Count} collections",
            undoSteps,
            selectAfter: importedNodes,
            selectAfterInverse: previousSelection));

        OnPropertyChanged(nameof(HasAnyDirty));
        StatusMessage = importedNodes.Count == 1
            ? $"Imported '{importedNodes[0].Name}' (Ctrl+S to persist)"
            : $"Imported {importedNodes.Count} collections (Ctrl+S to persist)";

        if (errors.Count > 0)
        {
            MessageBox.Show(
                $"Imported {importedNodes.Count} collection(s), with {errors.Count} skipped:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}",
                "Import",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Chooses Postman vs RequestExplorer native JSON from the file name and document shape.
    /// </summary>
    private static CollectionDefinition ParseImportedCollectionFile(string filePath, string json)
    {
        var preferPostman = PostmanCollectionMapper.IsPostmanCollectionFileName(filePath)
                            || PostmanCollectionMapper.LooksLikePostmanJson(json);

        if (preferPostman)
        {
            return ParsePostmanCollectionJson(json);
        }

        if (TryParseNativeCollectionJson(json, out var native))
        {
            return native;
        }

        // Generic .json that wasn't clearly native — try Postman as a fallback.
        return ParsePostmanCollectionJson(json);
    }

    private static CollectionDefinition ParsePostmanCollectionJson(string json)
    {
        var document = JsonSerializer.Deserialize<PostmanCollectionDocument>(
                           json,
                           PostmanCollectionMapper.JsonOptions)
                       ?? throw new InvalidOperationException("File is not a valid Postman collection.");
        return PostmanCollectionMapper.FromCollectionDocument(document);
    }

    private static bool TryParseNativeCollectionJson(string json, out CollectionDefinition collection)
    {
        try
        {
            collection = JsonSerializer.Deserialize<CollectionDefinition>(json, NativeCollectionJsonOptions)!;
            if (collection is null)
            {
                collection = null!;
                return false;
            }

            // Reject empty shells that aren't really collections.
            return !string.IsNullOrWhiteSpace(collection.Name)
                   || collection.Folders.Count > 0
                   || collection.Requests.Count > 0
                   || collection.Variables.Count > 0;
        }
        catch (JsonException)
        {
            collection = null!;
            return false;
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
    public bool TryMoveNode(TreeNodeViewModel source, TreeNodeViewModel? target, TreeDropPlacement placement) =>
        TryMoveNodes([source], target, placement);

    /// <summary>Moves one or more nodes to the drop target.</summary>
    public bool TryMoveNodes(
        IReadOnlyList<TreeNodeViewModel> sources,
        TreeNodeViewModel? target,
        TreeDropPlacement placement)
    {
        var batch = PruneRedundantDescendants(sources.Where(s => s is not null).Distinct().ToList());
        if (batch.Count == 0)
        {
            return false;
        }

        if (batch.Count == 1)
        {
            return TryMoveNodeCore(batch[0], target, placement);
        }

        foreach (var source in batch)
        {
            if (target is not null && (ReferenceEquals(source, target) || IsUnder(target, source)))
            {
                return false;
            }
        }

        // Preserve relative order in the shared parent when possible; otherwise selection order.
        batch = OrderBatchForMove(batch);

        _suppressMoveSideEffects = true;
        var undoSteps = new List<IUndoAction>();
        var movedAny = false;
        try
        {
            if (placement == TreeDropPlacement.Root || (target is null && placement == TreeDropPlacement.Root))
            {
                var insertAt = 0;
                foreach (var source in batch)
                {
                    if (TryMoveIdentityCollectUndo(source, parent: null, insertAt, undoSteps))
                    {
                        movedAny = true;
                        insertAt++;
                    }
                }
            }
            else if (target is null)
            {
                return false;
            }
            else if (placement == TreeDropPlacement.Into)
            {
                if (target is not (CollectionNodeViewModel or FolderNodeViewModel))
                {
                    return false;
                }

                if (batch.Any(s => s is CollectionNodeViewModel))
                {
                    return false;
                }

                foreach (var source in batch)
                {
                    if (TryMoveIdentityCollectUndo(source, target, target.Children.Count, undoSteps))
                    {
                        movedAny = true;
                    }
                }
            }
            else
            {
                var after = placement == TreeDropPlacement.After;
                if (!TryResolveBesideInsert(target, after, out var parent, out var insertAt))
                {
                    return false;
                }

                // Multi-select convert of root collections into folders is not supported.
                if (parent is not null && batch.Any(s => s is CollectionNodeViewModel))
                {
                    return false;
                }

                // Adjust insert index for selected nodes currently before the insertion point.
                var beforeCount = batch.Count(n =>
                {
                    if (!TryGetSiblingIndex(n, out var p, out var idx))
                    {
                        return false;
                    }

                    return ReferenceEquals(p, parent) && idx < insertAt;
                });
                insertAt -= beforeCount;

                foreach (var source in batch)
                {
                    if (TryMoveIdentityCollectUndo(source, parent, insertAt, undoSteps))
                    {
                        movedAny = true;
                        insertAt++;
                    }
                }
            }
        }
        finally
        {
            _suppressMoveSideEffects = false;
        }

        if (!movedAny)
        {
            return false;
        }

        if (undoSteps.Count > 0)
        {
            RecordUndo(new CompositeUndoAction(
                $"Move {batch.Count} items",
                undoSteps,
                selectAfter: batch,
                selectAfterInverse: batch));
        }

        ReplaceSelection(batch, batch[0]);
        SelectedNode = batch[0];
        foreach (var node in batch)
        {
            RefreshRowLayoutAfterMove(node);
        }

        StatusMessage = $"Moved {batch.Count} items (Ctrl+S to persist)";
        return true;
    }

    private bool TryMoveNodeCore(TreeNodeViewModel source, TreeNodeViewModel? target, TreeDropPlacement placement)
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
        MoveToNewSubFolderCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMoveToNewSubFolder))]
    private void MoveToNewSubFolder()
    {
        if (!TryGetMoveToNewSubFolderTarget(out var batch, out var parent, out var insertAt))
        {
            return;
        }

        if (!TryPromptName("Move to New Sub-folder", "Folder name:", "New Folder", out var name))
        {
            return;
        }

        var model = new FolderDefinition { Name = name };
        var folder = new FolderNodeViewModel(model);
        folder.AttachUndoRecorder(this);

        _suppressMoveSideEffects = true;
        var undoSteps = new List<IUndoAction>();
        var movedAny = false;
        try
        {
            InsertNodeCore(parent, folder, insertAt);
            parent.IsExpanded = true;
            folder.IsExpanded = true;

            foreach (var source in batch)
            {
                if (TryMoveIdentityCollectUndo(source, folder, folder.Children.Count, undoSteps))
                {
                    movedAny = true;
                }
            }
        }
        finally
        {
            _suppressMoveSideEffects = false;
        }

        if (!movedAny)
        {
            // Folder was created but nothing moved — remove it and abort.
            RemoveNodeCore(folder);
            StatusMessage = "Could not move selection into a new sub-folder";
            return;
        }

        undoSteps.Add(CreateRemoveAction(folder, $"Add folder '{name}'"));
        RecordUndo(new CompositeUndoAction(
            batch.Count == 1
                ? $"Move '{batch[0].Name}' to new sub-folder '{name}'"
                : $"Move {batch.Count} items to new sub-folder '{name}'",
            undoSteps,
            selectAfter: batch,
            selectAfterInverse: batch));

        ReplaceSelection(batch, batch[0]);
        SelectedNode = batch[0];
        RefreshRowLayoutAfterMove(folder);
        foreach (var node in batch)
        {
            RefreshRowLayoutAfterMove(node);
        }

        StatusMessage = batch.Count == 1
            ? $"Moved '{batch[0].Name}' into new folder '{name}' (Ctrl+S to persist)"
            : $"Moved {batch.Count} items into new folder '{name}' (Ctrl+S to persist)";
    }

    private bool CanMoveToNewSubFolder() =>
        TryGetMoveToNewSubFolderTarget(out _, out _, out _);

    /// <summary>
    /// Selection must be non-collection nodes that share one collection/folder parent.
    /// </summary>
    private bool TryGetMoveToNewSubFolderTarget(
        out List<TreeNodeViewModel> batch,
        out TreeNodeViewModel parent,
        out int insertAt)
    {
        batch = [];
        parent = null!;
        insertAt = 0;

        var raw = GetMoveBatch();
        if (raw.Count == 0)
        {
            return false;
        }

        foreach (var node in raw)
        {
            if (node is CollectionNodeViewModel)
            {
                return false;
            }
        }

        batch = OrderBatchForMove(raw);
        if (batch.Count == 0)
        {
            return false;
        }

        var first = batch[0];
        foreach (var node in batch)
        {
            if (!SharesSelectionParent(node, first))
            {
                return false;
            }
        }

        if (first.Parent is not (CollectionNodeViewModel or FolderNodeViewModel))
        {
            return false;
        }

        parent = first.Parent;
        var minIndex = parent.Children.Count;
        foreach (var node in batch)
        {
            var index = parent.Children.IndexOf(node);
            if (index >= 0 && index < minIndex)
            {
                minIndex = index;
            }
        }

        insertAt = minIndex;
        return true;
    }

    /// <summary>True when the current selection can be copied into a collection.</summary>
    public bool CanCopyToCollection() => GetMoveBatch().Count > 0;

    /// <summary>
    /// Collections that can receive the selection (excludes selected collections).
    /// </summary>
    public IReadOnlyList<CollectionNodeViewModel> GetCopyToCollectionDestinations()
    {
        var batch = GetMoveBatch();
        var result = new List<CollectionNodeViewModel>();
        foreach (var collection in Collections)
        {
            if (!IsInvalidCopyDestination(collection, batch))
            {
                result.Add(collection);
            }
        }

        return result;
    }

    private static bool IsInvalidCopyDestination(
        TreeNodeViewModel destination,
        IReadOnlyList<TreeNodeViewModel> batch)
    {
        foreach (var source in batch)
        {
            if (ReferenceEquals(source, destination) || IsUnder(destination, source))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Prompts for a new collection name, creates it, and copies the selection into it.
    /// Originals stay in place. Collections in the selection are copied as folders.
    /// </summary>
    public void CopySelectionToNewCollection()
    {
        var batch = OrderBatchForMove(GetMoveBatch());
        if (batch.Count == 0)
        {
            return;
        }

        if (!TryPromptName("New Collection", "Collection name:", "New Collection", out var name))
        {
            return;
        }

        var model = new CollectionDefinition { Name = name };
        var collection = new CollectionNodeViewModel(model);
        collection.AttachUndoRecorder(this);
        InsertNodeCore(null, collection, Collections.Count);

        if (!TryCopySourcesIntoParent(batch, collection, out var pasted, out var undoSteps))
        {
            RemoveNodeCore(collection);
            StatusMessage = "Could not copy selection into a new collection";
            return;
        }

        undoSteps.Insert(0, CreateRemoveAction(collection, $"Add collection '{name}'"));
        RecordUndo(new CompositeUndoAction(
            pasted.Count == 1
                ? $"Copy '{pasted[0].Name}' to new collection '{name}'"
                : $"Copy {pasted.Count} items to new collection '{name}'",
            undoSteps,
            selectAfter: pasted,
            selectAfterInverse: pasted));

        FinishCopyToParent(collection, pasted, name);
    }

    /// <summary>Copies the selection into an existing collection (collections become folders).</summary>
    public void CopySelectionToCollection(CollectionNodeViewModel target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var batch = OrderBatchForMove(GetMoveBatch());
        if (batch.Count == 0 || IsInvalidCopyDestination(target, batch))
        {
            return;
        }

        var sources = new List<TreeNodeViewModel>();
        foreach (var source in batch)
        {
            if (ReferenceEquals(source, target))
            {
                continue;
            }

            sources.Add(source);
        }

        if (sources.Count == 0)
        {
            return;
        }

        if (!TryCopySourcesIntoParent(sources, target, out var pasted, out var undoSteps))
        {
            StatusMessage = "Could not copy selection into collection";
            return;
        }

        RecordUndo(new CompositeUndoAction(
            pasted.Count == 1
                ? $"Copy '{pasted[0].Name}' to '{target.Name}'"
                : $"Copy {pasted.Count} items to '{target.Name}'",
            undoSteps,
            selectAfter: pasted,
            selectAfterInverse: pasted));

        FinishCopyToParent(target, pasted, target.Name);
    }

    private bool TryCopySourcesIntoParent(
        IReadOnlyList<TreeNodeViewModel> sources,
        TreeNodeViewModel target,
        out List<TreeNodeViewModel> pasted,
        out List<IUndoAction> undoSteps)
    {
        pasted = [];
        undoSteps = [];
        foreach (var source in sources)
        {
            var clone = CloneNodeIntoParent(source, target);
            if (clone is null)
            {
                continue;
            }

            pasted.Add(clone);
            undoSteps.Add(CreateRemoveAction(clone, $"Copy '{source.Name}'"));
        }

        return pasted.Count > 0;
    }

    private void FinishCopyToParent(
        TreeNodeViewModel target,
        List<TreeNodeViewModel> pasted,
        string destinationName)
    {
        target.IsExpanded = true;
        ReplaceSelection(pasted, pasted[0]);
        SelectedNode = pasted[0];
        foreach (var node in pasted)
        {
            RefreshRowLayoutAfterMove(node);
        }

        StatusMessage = pasted.Count == 1
            ? $"Copied '{pasted[0].Name}' into '{destinationName}' (Ctrl+S to persist)"
            : $"Copied {pasted.Count} items into '{destinationName}' (Ctrl+S to persist)";
    }

    private TreeNodeViewModel? CloneNodeIntoParent(TreeNodeViewModel source, TreeNodeViewModel parent)
    {
        PrepareNodeForExport(source);
        switch (source)
        {
            case RequestNodeViewModel request:
                return AttachPastedRequest(parent, TreeNodeCloneService.CloneRequest(request.Model));
            case FolderNodeViewModel folder:
                CollectionTreeMapper.SyncFolder(folder);
                return AttachPastedFolder(parent, TreeNodeCloneService.CloneFolder(folder.Model));
            case CollectionNodeViewModel collection:
                CollectionTreeMapper.SyncModelFromView(collection);
                return AttachPastedFolder(parent, TreeNodeCloneService.CollectionAsFolder(collection.Model));
            default:
                return null;
        }
    }

    private bool CanMoveSelectedAmongSiblings(int direction)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return false;
        }

        var batch = GetMoveBatch();
        if (batch.Count == 0)
        {
            return false;
        }

        if (batch.Count == 1)
        {
            return TryGetSibling(batch[0], direction, out _, out _);
        }

        return TryGetBlockMoveAnchor(batch, direction, out _, out _);
    }

    private bool TryMoveSelectedAmongSiblings(int direction)
    {
        var batch = GetMoveBatch();
        if (batch.Count == 0)
        {
            return false;
        }

        _deferTreeFilterDuringAltMove = true;
        bool moved;
        if (batch.Count == 1)
        {
            if (!TryGetSibling(batch[0], direction, out var sibling, out _))
            {
                _deferTreeFilterDuringAltMove = false;
                return false;
            }

            moved = MoveBeside(batch[0], sibling, after: direction > 0);
        }
        else
        {
            moved = TryMoveSelectionBlockAmongSiblings(batch, direction);
        }

        if (!moved)
        {
            _deferTreeFilterDuringAltMove = false;
            return false;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            EndAltMoveFilterDeferral();
        }

        return true;
    }

    private bool TryMoveSelectionBlockAmongSiblings(IReadOnlyList<TreeNodeViewModel> batch, int direction)
    {
        if (!TryGetBlockMoveAnchor(batch, direction, out var parent, out var insertAt))
        {
            return false;
        }

        var ordered = OrderBatchForMove(batch);
        var beforeCount = ordered.Count(n =>
        {
            if (!TryGetSiblingIndex(n, out var p, out var idx))
            {
                return false;
            }

            return ReferenceEquals(p, parent) && idx < insertAt;
        });
        insertAt -= beforeCount;

        _suppressMoveSideEffects = true;
        var undoSteps = new List<IUndoAction>();
        var movedAny = false;
        try
        {
            foreach (var source in ordered)
            {
                if (TryMoveIdentityCollectUndo(source, parent, insertAt, undoSteps))
                {
                    movedAny = true;
                    insertAt++;
                }
            }
        }
        finally
        {
            _suppressMoveSideEffects = false;
        }

        if (!movedAny)
        {
            return false;
        }

        RecordUndo(new CompositeUndoAction(
            $"Move {ordered.Count} items",
            undoSteps,
            selectAfter: ordered,
            selectAfterInverse: ordered));
        ReplaceSelection(ordered, ordered[0]);
        SelectedNode = ordered[0];
        foreach (var node in ordered)
        {
            RefreshRowLayoutAfterMove(node);
        }

        StatusMessage = $"Moved {ordered.Count} items (Ctrl+S to persist)";
        return true;
    }

    private bool TryGetBlockMoveAnchor(
        IReadOnlyList<TreeNodeViewModel> batch,
        int direction,
        out TreeNodeViewModel? parent,
        out int insertAt)
    {
        parent = null;
        insertAt = -1;
        if (batch.Count == 0 || !batch.All(n => SharesSelectionParent(n, batch[0])))
        {
            return false;
        }

        var ordered = OrderBatchForMove(batch);
        if (direction < 0)
        {
            if (!TryGetSibling(ordered[0], -1, out var sibling, out _))
            {
                return false;
            }

            // Skip siblings that are part of the selection.
            while (batch.Contains(sibling))
            {
                if (!TryGetSibling(sibling, -1, out sibling, out _))
                {
                    return false;
                }
            }

            return TryResolveBesideInsert(sibling, after: false, out parent, out insertAt);
        }

        if (!TryGetSibling(ordered[^1], 1, out var next, out _))
        {
            return false;
        }

        while (batch.Contains(next))
        {
            if (!TryGetSibling(next, 1, out next, out _))
            {
                return false;
            }
        }

        return TryResolveBesideInsert(next, after: true, out parent, out insertAt);
    }

    private List<TreeNodeViewModel> OrderBatchForMove(IReadOnlyList<TreeNodeViewModel> batch)
    {
        if (batch.Count <= 1)
        {
            return batch.ToList();
        }

        if (batch.All(n => SharesSelectionParent(n, batch[0])))
        {
            return batch
                .Select(n => (Node: n, Index: TryGetSiblingIndex(n, out _, out var idx) ? idx : int.MaxValue))
                .OrderBy(t => t.Index)
                .Select(t => t.Node)
                .ToList();
        }

        return batch.ToList();
    }

    private bool TryResolveBesideInsert(
        TreeNodeViewModel target,
        bool after,
        out TreeNodeViewModel? parent,
        out int insertAt)
    {
        parent = null;
        insertAt = -1;

        if (target is CollectionNodeViewModel && target.Parent is null)
        {
            insertAt = Collections.IndexOf((CollectionNodeViewModel)target);
            if (insertAt < 0)
            {
                return false;
            }

            if (after)
            {
                insertAt++;
            }

            return true;
        }

        parent = target.Parent;
        if (parent is null)
        {
            return false;
        }

        insertAt = parent.Children.IndexOf(target);
        if (insertAt < 0)
        {
            return false;
        }

        if (after)
        {
            insertAt++;
        }

        return true;
    }

    private bool TryGetSiblingIndex(TreeNodeViewModel node, out TreeNodeViewModel? parent, out int index)
    {
        if (node is CollectionNodeViewModel collection && node.Parent is null)
        {
            parent = null;
            index = Collections.IndexOf(collection);
            return index >= 0;
        }

        parent = node.Parent;
        if (parent is null)
        {
            index = -1;
            return false;
        }

        index = parent.Children.IndexOf(node);
        return index >= 0;
    }

    private bool TryMoveIdentityCollectUndo(
        TreeNodeViewModel source,
        TreeNodeViewModel? parent,
        int index,
        List<IUndoAction> undoSteps)
    {
        var oldParent = source.Parent;
        var oldIndex = oldParent is not null
            ? oldParent.Children.IndexOf(source)
            : Collections.IndexOf((CollectionNodeViewModel)source);

        if (oldIndex < 0 && source is not CollectionNodeViewModel)
        {
            return false;
        }

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

        RemoveNodeCore(source);

        if (parent is null)
        {
            if (source is not CollectionNodeViewModel)
            {
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

        undoSteps.Add(new RemoveNodeAction(source, parent, index, $"Move '{source.Name}'"));
        undoSteps.Add(new InsertNodeAction(oldParent, source, oldIndex, $"Move '{source.Name}'"));
        return true;
    }

    /// <summary>
    /// Finds the next/previous <see cref="TreeNodeViewModel.IsVisibleInFilter"/> sibling
    /// (skipping nodes hidden by the active tree filter).
    /// </summary>
    private bool TryGetSibling(
        TreeNodeViewModel node,
        int direction,
        out TreeNodeViewModel sibling,
        out int index)
    {
        sibling = null!;
        index = -1;

        if (direction == 0)
        {
            return false;
        }

        if (node is CollectionNodeViewModel collection && node.Parent is null)
        {
            index = Collections.IndexOf(collection);
            if (index < 0)
            {
                return false;
            }

            for (var i = index + direction; i >= 0 && i < Collections.Count; i += direction)
            {
                if (Collections[i].IsVisibleInFilter)
                {
                    sibling = Collections[i];
                    return true;
                }
            }

            return false;
        }

        var parent = node.Parent;
        if (parent is null)
        {
            return false;
        }

        index = parent.Children.IndexOf(node);
        if (index < 0)
        {
            return false;
        }

        for (var i = index + direction; i >= 0 && i < parent.Children.Count; i += direction)
        {
            if (parent.Children[i].IsVisibleInFilter)
            {
                sibling = parent.Children[i];
                return true;
            }
        }

        return false;
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
            ],
            selectAfter: [source],
            selectAfterInverse: [newNode]));

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
            ],
            selectAfter: [collection],
            selectAfterInverse: [folderNode]));

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

        if (!_suppressMoveSideEffects)
        {
            SelectedNode = source;
            RefreshRowLayoutAfterMove(source);
            RecordUndo(new CompositeUndoAction(
                $"Move '{source.Name}'",
                [
                    new RemoveNodeAction(source, parent, index, $"Move '{source.Name}'"),
                    new InsertNodeAction(oldParent, source, oldIndex, $"Move '{source.Name}'")
                ],
                selectAfter: [source],
                selectAfterInverse: [source]));
            StatusMessage = $"Moved '{source.Name}' (Ctrl+S to persist)";
        }

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

    public bool ConfirmClose() =>
        ConfirmSaveIfDirty("You have unsaved changes. Save all before closing?");

    /// <summary>
    /// When there are dirty nodes, prompts to save / discard / cancel.
    /// Returns false if the caller should abort.
    /// </summary>
    private bool ConfirmSaveIfDirty(string message)
    {
        if (!HasAnyDirty)
        {
            return true;
        }

        var choice = Views.ConfirmSaveDialog.Prompt(Application.Current?.MainWindow, message);

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

    [RelayCommand]
    private void ReloadFromDisk()
    {
        if (!ConfirmSaveIfDirty("You have unsaved changes. Save all before reloading from disk?"))
        {
            return;
        }

        ReloadWorkspaceFromDisk("Reloaded");
    }

    private void ReloadWorkspaceFromDisk(string statusPrefix)
    {
        var priorOrder = Collections.Select(c => c.Model.Id).ToList();
        CaptureExpandedTreeNodes();

        SelectedNode = null;
        _treeClipboard = null;
        OnPropertyChanged(nameof(HasTreeClipboard));
        _history.Clear();
        RefreshHistoryMenus();

        // New disk collections (absent from priorOrder) are prepended by ApplyCollectionOrder.
        LoadWorkspace(priorOrder);
        ApplyTreeFilter();
        OnPropertyChanged(nameof(HasAnyDirty));
        StatusMessage = $"{statusPrefix} — {Collections.Count} collection(s) from disk";
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
