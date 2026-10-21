using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.Models;

namespace RequestExplorer.ViewModels.Nodes;

public sealed partial class FolderNodeViewModel : ContainerNodeViewModel
{
    public FolderNodeViewModel(FolderDefinition model)
        : base(model.Name, model.Variables)
    {
        Model = model;
        _optOutOfAutoRun = model.OptOutOfAutoRun;
        Children.CollectionChanged += OnChildrenChanged;
    }

    public override TreeNodeKind Kind => TreeNodeKind.Folder;

    public FolderDefinition Model { get; }

    public override bool LocallyOptsOutOfAutoRun => OptOutOfAutoRun;

    [ObservableProperty]
    private bool _optOutOfAutoRun;

    partial void OnOptOutOfAutoRunChanged(bool value)
    {
        if (Model.OptOutOfAutoRun != value)
        {
            Model.OptOutOfAutoRun = value;
            IsDirty = true;
        }

        NotifyAutoRunExclusionChanged();
    }

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
