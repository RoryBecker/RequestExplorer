using System.Collections.Specialized;
using RequestExplorer.Models;

namespace RequestExplorer.ViewModels.Nodes;

public sealed class CollectionNodeViewModel : ContainerNodeViewModel
{
    public CollectionNodeViewModel(CollectionDefinition model)
        : base(model.Name, model.Variables)
    {
        Model = model;
        Children.CollectionChanged += OnChildrenChanged;
    }

    public override TreeNodeKind Kind => TreeNodeKind.Collection;

    public CollectionDefinition Model { get; }

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

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ChildFolderCount));
        OnPropertyChanged(nameof(ChildRequestCount));
    }
}
