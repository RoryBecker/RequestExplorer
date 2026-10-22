using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Undo;

public sealed class CompositeUndoAction : IUndoAction
{
    private readonly IReadOnlyList<IUndoAction> _steps;
    private readonly IReadOnlyList<TreeNodeViewModel>? _selectAfter;
    private readonly IReadOnlyList<TreeNodeViewModel>? _selectAfterInverse;

    public CompositeUndoAction(
        string description,
        IEnumerable<IUndoAction> steps,
        IEnumerable<TreeNodeViewModel>? selectAfter = null,
        IEnumerable<TreeNodeViewModel>? selectAfterInverse = null)
    {
        Description = description;
        _steps = steps.ToList();
        _selectAfter = selectAfter?.ToList();
        _selectAfterInverse = selectAfterInverse?.ToList() ?? _selectAfter;
    }

    public string Description { get; }

    public long HistoryId { get; set; }

    public IEnumerable<TreeNodeViewModel> GetAffectedNodes() =>
        _steps.SelectMany(s => s.GetAffectedNodes())
            .Concat(_selectAfter ?? [])
            .Concat(_selectAfterInverse ?? [])
            .Distinct();

    public IReadOnlyList<IUndoAction> Steps => _steps;

    public IReadOnlyList<TreeNodeViewModel>? SelectAfter => _selectAfter;

    public IReadOnlyList<TreeNodeViewModel>? SelectAfterInverse => _selectAfterInverse;

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

        if (_selectAfter is { Count: > 0 })
        {
            context.RevealAndSelect(_selectAfter);
        }

        return new CompositeUndoAction(Description, inverses, _selectAfterInverse, _selectAfter);
    }
}
