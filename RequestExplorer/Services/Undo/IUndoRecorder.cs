namespace RequestExplorer.Services.Undo;

public interface IUndoRecorder
{
    bool IsApplyingHistory { get; }

    void Record(IUndoAction action);
}
