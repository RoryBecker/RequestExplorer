namespace RequestExplorer.Services.Undo;

/// <summary>
/// Undo actions that can merge consecutive edits (e.g. typing) into one stack entry.
/// The existing top entry keeps the earliest prior value; the incoming edit is dropped.
/// </summary>
public interface ICoalescingUndoAction : IUndoAction
{
    bool CanCoalesceWith(IUndoAction incoming);
}
