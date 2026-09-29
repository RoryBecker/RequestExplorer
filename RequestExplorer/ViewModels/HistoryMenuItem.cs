namespace RequestExplorer.ViewModels;

public sealed class HistoryMenuItem
{
    public HistoryMenuItem(int depth, string description)
    {
        Depth = depth;
        Description = description;
    }

    /// <summary>How many stack entries to undo/redo when this item is chosen (1 = top only).</summary>
    public int Depth { get; }

    public string Description { get; }
}
