namespace RequestExplorer.Services.Undo;

public sealed class CompositeUndoAction : IUndoAction
{
    private readonly IReadOnlyList<IUndoAction> _steps;

    public CompositeUndoAction(string description, IEnumerable<IUndoAction> steps)
    {
        Description = description;
        _steps = steps.ToList();
    }

    public string Description { get; }

    public UndoRevealTarget? RevealTarget =>
        _steps.Select(s => s.RevealTarget).FirstOrDefault(t => t is not null);

    public IUndoAction Execute(IUndoContext context)
    {
        var inverses = new List<IUndoAction>(_steps.Count);
        foreach (var step in _steps)
        {
            inverses.Add(step.Execute(context));
        }

        inverses.Reverse();
        return new CompositeUndoAction(Description, inverses);
    }
}
