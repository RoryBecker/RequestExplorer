using System.ComponentModel;

namespace RequestExplorer.Services.Undo;

public sealed class UndoStack : INotifyPropertyChanged
{
    private readonly Stack<IUndoAction> _undo = new();
    private readonly Stack<IUndoAction> _redo = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string UndoDescription =>
        _undo.TryPeek(out var action) ? action.Description : "Undo";

    public string RedoDescription =>
        _redo.TryPeek(out var action) ? action.Description : "Redo";

    public bool TryPeekUndo(out IUndoAction action) => _undo.TryPeek(out action!);

    public bool TryPeekRedo(out IUndoAction action) => _redo.TryPeek(out action!);

    public void Push(IUndoAction undoInstruction)
    {
        ArgumentNullException.ThrowIfNull(undoInstruction);
        _undo.Push(undoInstruction);
        _redo.Clear();
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
        _undo.Push(inverse);
        Notify();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Notify();
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanUndo)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRedo)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UndoDescription)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RedoDescription)));
    }
}
