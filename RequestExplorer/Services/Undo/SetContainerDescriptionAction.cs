using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class SetContainerDescriptionAction : IUndoAction, ICoalescingUndoAction
{
    private readonly ContainerNodeViewModel _container;
    private readonly string _value;

    public SetContainerDescriptionAction(
        ContainerNodeViewModel container,
        string value,
        string description)
    {
        _container = container;
        _value = value;
        Description = description;
    }

    public ContainerNodeViewModel Container => _container;

    public string Value => _value;

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() => [_container];

    public UndoRevealTarget? RevealTarget => null;

    public bool CanCoalesceWith(IUndoAction incoming) =>
        incoming is SetContainerDescriptionAction other
        && ReferenceEquals(_container, other._container);

    public IUndoAction Execute(IUndoContext context)
    {
        var previous = _container.Description;
        _container.ApplyDescription(_value);
        context.Select(_container);
        return new SetContainerDescriptionAction(_container, previous, Description);
    }
}
