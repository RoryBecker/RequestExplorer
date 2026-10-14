using RequestExplorer.Services.Undo;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Services.Variables;

/// <summary>
/// Relocates key/value rows between Params/Headers and Variables scopes without renaming or rewriting values.
/// </summary>
public static class VariableScopeMove
{
    public static IEnumerable<ContainerNodeViewModel> EnumerateAncestorContainers(TreeNodeViewModel node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ContainerNodeViewModel container)
            {
                yield return container;
            }
        }
    }

    public static IEnumerable<ContainerNodeViewModel> EnumerateDirectChildFolders(ContainerNodeViewModel container) =>
        container.Children.OfType<FolderNodeViewModel>();

    /// <summary>
    /// Moves a param/header row onto a container as a variable. Returns false if cancelled or ineligible.
    /// </summary>
    public static bool TryMoveRequestRowToVariable(
        RequestNodeViewModel request,
        KeyValueListKind listKind,
        KeyValueItemViewModel item,
        ContainerNodeViewModel target,
        IUndoRecorder? undoRecorder,
        Func<string, string, string, string, bool> confirmOverwrite)
    {
        if (!item.HasKey)
        {
            return false;
        }

        var key = item.Key.Trim();
        var value = item.Value ?? string.Empty;
        var enabled = item.IsEnabled;
        var sourceIndex = request.IndexOfKeyValue(listKind, item);
        if (sourceIndex < 0)
        {
            return false;
        }

        var existing = target.FindVariableByKey(key);
        if (existing is not null)
        {
            if (!confirmOverwrite(key, target.Name, existing.Value ?? string.Empty, value))
            {
                return false;
            }

            var oldValue = existing.Value ?? string.Empty;
            var oldEnabled = existing.IsEnabled;
            request.RemoveKeyValueRow(listKind, item);
            target.ApplyVariableField(existing, KeyValueField.Value, value);
            target.ApplyVariableField(existing, KeyValueField.IsEnabled, enabled && existing.HasKey);

            var label = listKind == KeyValueListKind.Params ? "param" : "header";
            RecordUndo(
                undoRecorder,
                $"Move {label} '{key}' to '{target.Name}' (overwrite)",
                [
                    new SetVariableFieldAction(target, existing, KeyValueField.Value, oldValue, "Restore variable value"),
                    new SetVariableFieldAction(target, existing, KeyValueField.IsEnabled, oldEnabled, "Restore variable enabled"),
                    new InsertKeyValueRowAction(request, listKind, item, sourceIndex, $"Restore {label}")
                ]);
            target.RefreshVariablePresentation();
            return true;
        }

        request.RemoveKeyValueRow(listKind, item);
        var inserted = target.InsertVariableSnapshot(key, value, enabled, out var insertIndex);
        var labelNew = listKind == KeyValueListKind.Params ? "param" : "header";
        RecordUndo(
            undoRecorder,
            $"Move {labelNew} '{key}' to '{target.Name}'",
            [
                new RemoveVariableRowAction(target, inserted, insertIndex, "Remove moved variable"),
                new InsertKeyValueRowAction(request, listKind, item, sourceIndex, $"Restore {labelNew}")
            ]);
        target.RefreshVariablePresentation();
        return true;
    }

    /// <summary>
    /// Moves a local variable row to another container. Returns false if cancelled or ineligible.
    /// </summary>
    public static bool TryMoveVariableToContainer(
        ContainerNodeViewModel source,
        KeyValueItemViewModel item,
        ContainerNodeViewModel target,
        IUndoRecorder? undoRecorder,
        Func<string, string, string, string, bool> confirmOverwrite)
    {
        if (!item.HasKey || ReferenceEquals(source, target))
        {
            return false;
        }

        var key = item.Key.Trim();
        var value = item.Value ?? string.Empty;
        var enabled = item.IsEnabled;
        var sourceIndex = source.IndexOfVariable(item);
        if (sourceIndex < 0)
        {
            return false;
        }

        var existing = target.FindVariableByKey(key);
        if (existing is not null)
        {
            if (!confirmOverwrite(key, target.Name, existing.Value ?? string.Empty, value))
            {
                return false;
            }

            var oldValue = existing.Value ?? string.Empty;
            var oldEnabled = existing.IsEnabled;
            source.RemoveVariableRow(item);
            target.ApplyVariableField(existing, KeyValueField.Value, value);
            target.ApplyVariableField(existing, KeyValueField.IsEnabled, enabled && existing.HasKey);

            RecordUndo(
                undoRecorder,
                $"Move variable '{key}' to '{target.Name}' (overwrite)",
                [
                    new SetVariableFieldAction(target, existing, KeyValueField.Value, oldValue, "Restore variable value"),
                    new SetVariableFieldAction(target, existing, KeyValueField.IsEnabled, oldEnabled, "Restore variable enabled"),
                    new InsertVariableRowAction(source, item, sourceIndex, "Restore variable")
                ]);
            source.RefreshVariablePresentation();
            target.RefreshVariablePresentation();
            return true;
        }

        source.RemoveVariableRow(item);
        var inserted = target.InsertVariableSnapshot(key, value, enabled, out var insertIndex);
        RecordUndo(
            undoRecorder,
            $"Move variable '{key}' to '{target.Name}'",
            [
                new RemoveVariableRowAction(target, inserted, insertIndex, "Remove moved variable"),
                new InsertVariableRowAction(source, item, sourceIndex, "Restore variable")
            ]);
        source.RefreshVariablePresentation();
        target.RefreshVariablePresentation();
        return true;
    }

    private static void RecordUndo(IUndoRecorder? undoRecorder, string description, IEnumerable<IUndoAction> undoSteps)
    {
        if (undoRecorder is null || undoRecorder.IsApplyingHistory)
        {
            return;
        }

        undoRecorder.Record(new CompositeUndoAction(description, undoSteps));
    }
}
