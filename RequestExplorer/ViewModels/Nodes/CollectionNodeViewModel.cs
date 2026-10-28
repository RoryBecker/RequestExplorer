using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.Models;
using RequestExplorer.Services.Undo;

namespace RequestExplorer.ViewModels.Nodes;

public sealed partial class CollectionNodeViewModel : ContainerNodeViewModel
{
    public CollectionNodeViewModel(CollectionDefinition model)
        : base(model.Name, model.Variables, model.Description)
    {
        Model = model;
        _optOutOfAutoRun = model.OptOutOfAutoRun;
        Children.CollectionChanged += OnChildrenChanged;
    }

    public override TreeNodeKind Kind => TreeNodeKind.Collection;

    public CollectionDefinition Model { get; }

    public override bool LocallyOptsOutOfAutoRun => OptOutOfAutoRun;

    [ObservableProperty]
    private bool _optOutOfAutoRun;

    public void ApplyOptOutOfAutoRun(bool value)
    {
        SuppressFieldUndo = true;
        try
        {
            OptOutOfAutoRun = value;
        }
        finally
        {
            SuppressFieldUndo = false;
        }
    }

    partial void OnOptOutOfAutoRunChanged(bool value)
    {
        var previous = Model.OptOutOfAutoRun;
        if (previous == value)
        {
            NotifyAutoRunExclusionChanged();
            return;
        }

        Model.OptOutOfAutoRun = value;
        if (UndoRecorder?.IsApplyingHistory != true)
        {
            IsDirty = true;
        }

        RecordOptOutUndo(previous);
        NotifyAutoRunExclusionChanged();
    }

    private void RecordOptOutUndo(bool previous)
    {
        if (SuppressFieldUndo || UndoRecorder is null || UndoRecorder.IsApplyingHistory)
        {
            return;
        }

        UndoRecorder.Record(
            new SetOptOutOfAutoRunAction(this, previous, "Toggle opt out of auto run"));
    }

    /// <summary>
    /// Undo-stack history id of the last save of this collection to disk.
    /// 0 is the origin (loaded from disk / never edited).
    /// </summary>
    public long DiskHistoryId { get; set; }

    /// <summary>
    /// Nodes that diverged from disk because the save checkpoint was discarded
    /// (undo past save, then a new edit cleared redo).
    /// </summary>
    public HashSet<Guid> DivergedNodeIds { get; } = [];

    public int ChildFolderCount => Children.Count(c => c is FolderNodeViewModel);

    public int ChildRequestCount => Children.Count(c => c is RequestNodeViewModel);

    protected override void OnNameUpdated(string value)
    {
        Model.Name = value;
        if (UndoRecorder?.IsApplyingHistory == true)
        {
            return;
        }

        IsDirty = true;
    }

    protected override string? ReadModelDescription() => Model.Description;

    protected override void WriteModelDescription(string? value) => Model.Description = value;

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ChildFolderCount));
        OnPropertyChanged(nameof(ChildRequestCount));
    }
}
