using System.Collections.Specialized;
using RequestExplorer.Models;

namespace RequestExplorer.ViewModels;

public sealed class FolderNodeViewModel : ContainerNodeViewModel
{
    public FolderNodeViewModel(FolderDefinition model)
        : base(model.Name, model.Variables)
    {
        Model = model;
        Children.CollectionChanged += OnChildrenChanged;
    }

    public override TreeNodeKind Kind => TreeNodeKind.Folder;

    public FolderDefinition Model { get; }

    public int ChildFolderCount => Children.Count(c => c is FolderNodeViewModel);

    public int ChildRequestCount => Children.Count(c => c is RequestNodeViewModel);

    protected override void OnNameUpdated(string value)
    {
        Model.Name = value;
        IsDirty = true;
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ChildFolderCount));
        OnPropertyChanged(nameof(ChildRequestCount));
    }
}
