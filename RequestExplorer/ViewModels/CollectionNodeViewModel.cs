using RequestExplorer.Models;

namespace RequestExplorer.ViewModels;

public sealed class CollectionNodeViewModel : TreeNodeViewModel
{
    public CollectionNodeViewModel(CollectionDefinition model)
        : base(model.Name)
    {
        Model = model;
    }

    public override TreeNodeKind Kind => TreeNodeKind.Collection;

    public CollectionDefinition Model { get; }

    protected override void OnNameUpdated(string value) => Model.Name = value;
}
