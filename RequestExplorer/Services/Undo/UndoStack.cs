using System.ComponentModel;

namespace RequestExplorer.Services.Undo;

public sealed class UndoStack : INotifyPropertyChanged
{
    private readonly Stack<IUndoAction> _undo = new();
    private readonly Stack<IUndoAction> _redo = new();
    private long _nextHistoryId = 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised with the redo entries about to be dropped when a new edit is pushed.</summary>
    public event Action<IReadOnlyList<IUndoAction>>? RedoDiscarding;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string UndoDescription =>
        _undo.TryPeek(out var action) ? action.Description : "Undo";

    public string RedoDescription =>
        _redo.TryPeek(out var action) ? action.Description : "Redo";

    /// <summary>History id of the current tree state, or 0 when the undo stack is empty.</summary>
    public long CurrentHistoryId =>
        _undo.TryPeek(out var action) ? action.HistoryId : 0;

    public long NextHistoryId => _nextHistoryId;

    public bool TryPeekUndo(out IUndoAction action) => _undo.TryPeek(out action!);

    public bool TryPeekRedo(out IUndoAction action) => _redo.TryPeek(out action!);

    public IReadOnlyList<string> GetUndoDescriptions(int max = 25) =>
        _undo.Take(Math.Max(0, max)).Select(a => a.Description).ToList();

    public IReadOnlyList<string> GetRedoDescriptions(int max = 25) =>
        _redo.Take(Math.Max(0, max)).Select(a => a.Description).ToList();

    public void Push(IUndoAction undoInstruction)
    {
        ArgumentNullException.ThrowIfNull(undoInstruction);

        // Coalesce rapid consecutive edits (PropertyChanged typing) into one undo step.
        if (_undo.TryPeek(out var top)
            && top is ICoalescingUndoAction coalescing
            && coalescing.CanCoalesceWith(undoInstruction))
        {
            DiscardRedo();
            Notify();
            return;
        }

        if (undoInstruction.HistoryId == 0)
        {
            undoInstruction.HistoryId = _nextHistoryId++;
        }
        else if (undoInstruction.HistoryId >= _nextHistoryId)
        {
            _nextHistoryId = undoInstruction.HistoryId + 1;
        }

        DiscardRedo();
        _undo.Push(undoInstruction);
        Notify();
    }

    public void Undo(IUndoContext context)
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var action = _undo.Pop();
        var inverse = action.Execute(context);
        inverse.HistoryId = action.HistoryId;
        _redo.Push(inverse);
        Notify();
    }

    public void Redo(IUndoContext context)
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var action = _redo.Pop();
        var inverse = action.Execute(context);
        inverse.HistoryId = action.HistoryId;
        _undo.Push(inverse);
        Notify();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Notify();
    }

    /// <summary>Undo entries from oldest to newest (bottom of stack first).</summary>
    public IReadOnlyList<IUndoAction> SnapshotUndoOldestFirst() => SnapshotOldestFirst(_undo);

    /// <summary>Redo entries from oldest to newest (bottom of stack first).</summary>
    public IReadOnlyList<IUndoAction> SnapshotRedoOldestFirst() => SnapshotOldestFirst(_redo);

    /// <summary>
    /// Operations on the path from the current position to
    /// <paramref name="diskHistoryId"/>. Empty when they match.
    /// Null when that checkpoint is no longer on either stack.
    /// </summary>
    public IReadOnlyList<IUndoAction>? GetActionsBetweenCurrentAnd(long diskHistoryId)
    {
        if (CurrentHistoryId == diskHistoryId)
        {
            return [];
        }

        var undo = _undo.ToArray();
        for (var i = 0; i < undo.Length; i++)
        {
            if (undo[i].HistoryId == diskHistoryId)
            {
                return undo.Take(i).ToList();
            }
        }

        if (diskHistoryId == 0)
        {
            return undo;
        }

        var redo = _redo.ToArray();
        for (var i = 0; i < redo.Length; i++)
        {
            if (redo[i].HistoryId == diskHistoryId)
            {
                return redo.Take(i + 1).ToList();
            }
        }

        return null;
    }

    public void Restore(
        IEnumerable<IUndoAction> undoOldestFirst,
        IEnumerable<IUndoAction> redoOldestFirst,
        long nextHistoryId = 0)
    {
        _undo.Clear();
        _redo.Clear();
        var maxId = 0L;
        foreach (var action in undoOldestFirst)
        {
            maxId = Math.Max(maxId, action.HistoryId);
            _undo.Push(action);
        }

        foreach (var action in redoOldestFirst)
        {
            maxId = Math.Max(maxId, action.HistoryId);
            _redo.Push(action);
        }

        _nextHistoryId = nextHistoryId > maxId ? nextHistoryId : maxId + 1;
        if (_nextHistoryId < 1)
        {
            _nextHistoryId = 1;
        }

        Notify();
    }

    private void DiscardRedo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        RedoDiscarding?.Invoke(_redo.ToArray());
        _redo.Clear();
    }

    private static IReadOnlyList<IUndoAction> SnapshotOldestFirst(Stack<IUndoAction> stack)
    {
        var items = stack.ToArray();
        Array.Reverse(items);
        return items;
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanUndo)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRedo)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UndoDescription)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RedoDescription)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentHistoryId)));
    }
}
