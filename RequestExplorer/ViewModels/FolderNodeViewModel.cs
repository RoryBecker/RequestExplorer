using RequestExplorer.Models;

namespace RequestExplorer.ViewModels;

public sealed class FolderNodeViewModel : TreeNodeViewModel
{
    public FolderNodeViewModel(FolderDefinition model)
        : base(model.Name)
    {
        Model = model;
    }

    public override TreeNodeKind Kind => TreeNodeKind.Folder;

    public FolderDefinition Model { get; }

    protected override void OnNameUpdated(string value) => Model.Name = value;
}
