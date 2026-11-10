using RequestExplorer.Services.Undo;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Headers;

/// <summary>
/// Relocates header rows between requests and container Headers lists (not Variables).
/// </summary>
public static class HeaderScopeMove
{
    public static bool TryMoveRequestHeaderToContainer(
        RequestNodeViewModel request,
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
        var sourceIndex = request.IndexOfKeyValue(KeyValueListKind.Headers, item);
        if (sourceIndex < 0)
        {
            return false;
        }

        var existing = target.FindHeaderByKey(key);
        if (existing is not null)
        {
            if (!confirmOverwrite(key, target.Name, existing.Value ?? string.Empty, value))
            {
                return false;
            }

            var oldValue = existing.Value ?? string.Empty;
            var oldEnabled = existing.IsEnabled;
            request.RemoveKeyValueRow(KeyValueListKind.Headers, item);
            target.ApplyHeaderField(existing, KeyValueField.Value, value);
            target.ApplyHeaderField(existing, KeyValueField.IsEnabled, enabled && existing.HasKey);

            RecordUndo(
                undoRecorder,
                $"Move header '{key}' to '{target.Name}' (overwrite)",
                [
                    new SetHeaderFieldAction(target, existing, KeyValueField.Value, oldValue, "Restore header value"),
                    new SetHeaderFieldAction(target, existing, KeyValueField.IsEnabled, oldEnabled, "Restore header enabled"),
                    new InsertKeyValueRowAction(request, KeyValueListKind.Headers, item, sourceIndex, "Restore header")
                ]);
            target.RefreshHeaderPresentation();
            request.RefreshInheritedHeaders();
            return true;
        }

        request.RemoveKeyValueRow(KeyValueListKind.Headers, item);
        var inserted = target.InsertHeaderSnapshot(key, value, enabled, out var insertIndex);
        RecordUndo(
            undoRecorder,
            $"Move header '{key}' to '{target.Name}'",
            [
                new RemoveHeaderRowAction(target, inserted, insertIndex, "Remove moved header"),
                new InsertKeyValueRowAction(request, KeyValueListKind.Headers, item, sourceIndex, "Restore header")
            ]);
        target.RefreshHeaderPresentation();
        request.RefreshInheritedHeaders();
        return true;
    }

    public static bool TryMoveHeaderToContainer(
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
        var isInheritable = item.IsInheritable;
        var sourceIndex = source.IndexOfHeader(item);
        if (sourceIndex < 0)
        {
            return false;
        }

        var existing = target.FindHeaderByKey(key);
        if (existing is not null)
        {
            if (!confirmOverwrite(key, target.Name, existing.Value ?? string.Empty, value))
            {
                return false;
            }

            var oldValue = existing.Value ?? string.Empty;
            var oldEnabled = existing.IsEnabled;
            var oldInheritable = existing.IsInheritable;
            source.RemoveHeaderRow(item);
            target.ApplyHeaderField(existing, KeyValueField.Value, value);
            target.ApplyHeaderField(existing, KeyValueField.IsEnabled, enabled && existing.HasKey);
            target.ApplyHeaderField(existing, KeyValueField.IsInheritable, isInheritable || !existing.HasKey);

            RecordUndo(
                undoRecorder,
                $"Move header '{key}' to '{target.Name}' (overwrite)",
                [
                    new SetHeaderFieldAction(target, existing, KeyValueField.Value, oldValue, "Restore header value"),
                    new SetHeaderFieldAction(target, existing, KeyValueField.IsEnabled, oldEnabled, "Restore header enabled"),
                    new SetHeaderFieldAction(target, existing, KeyValueField.IsInheritable, oldInheritable, "Restore header inherit"),
                    new InsertHeaderRowAction(source, item, sourceIndex, "Restore header")
                ]);
            source.RefreshHeaderPresentation();
            target.RefreshHeaderPresentation();
            return true;
        }

        source.RemoveHeaderRow(item);
        var inserted = target.InsertHeaderSnapshot(key, value, enabled, out var insertIndex, isInheritable);
        RecordUndo(
            undoRecorder,
            $"Move header '{key}' to '{target.Name}'",
            [
                new RemoveHeaderRowAction(target, inserted, insertIndex, "Remove moved header"),
                new InsertHeaderRowAction(source, item, sourceIndex, "Restore header")
            ]);
        source.RefreshHeaderPresentation();
        target.RefreshHeaderPresentation();
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
