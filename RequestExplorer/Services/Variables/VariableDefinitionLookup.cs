using RequestExplorer.Services.Undo;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

/// <summary>Finds the collection/folder row that defines a <c>{{name}}</c> token.</summary>
public static class VariableDefinitionLookup
{
    /// <summary>
    /// Collection or folder that should own a new definition: the request's parent,
    /// or the container itself when editing a collection/folder.
    /// </summary>
    public static ContainerNodeViewModel? ResolveOwner(TreeNodeViewModel? from) =>
        from is RequestNodeViewModel request
            ? request.Parent as ContainerNodeViewModel
            : from as ContainerNodeViewModel;

    public static bool TryFind(
        TreeNodeViewModel? from,
        string? name,
        out ContainerNodeViewModel container,
        out KeyValueItemViewModel item)
    {
        container = null!;
        item = null!;
        if (from is null || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var key = name.Trim();
        var node = from is RequestNodeViewModel ? from.Parent : from;
        ContainerNodeViewModel? disabledContainer = null;
        KeyValueItemViewModel? disabledItem = null;

        for (; node is not null; node = node.Parent)
        {
            if (node is not ContainerNodeViewModel candidate)
            {
                continue;
            }

            var found = candidate.FindVariableByKey(key);
            if (found is null)
            {
                continue;
            }

            if (found.IsEnabled)
            {
                container = candidate;
                item = found;
                return true;
            }

            disabledContainer ??= candidate;
            disabledItem ??= found;
        }

        if (disabledContainer is not null && disabledItem is not null)
        {
            container = disabledContainer;
            item = disabledItem;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Inserts an enabled variable on <see cref="ResolveOwner"/> when that key is not already defined there.
    /// </summary>
    public static bool TryCreate(
        TreeNodeViewModel? from,
        string? name,
        string? value,
        IUndoRecorder? undoRecorder,
        out ContainerNodeViewModel owner,
        out KeyValueItemViewModel item)
    {
        owner = null!;
        item = null!;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var key = name.Trim();
        if (key.Contains("{{", StringComparison.Ordinal) || key.Contains("}}", StringComparison.Ordinal))
        {
            return false;
        }

        var resolved = ResolveOwner(from);
        if (resolved is null || resolved.FindVariableByKey(key) is not null)
        {
            return false;
        }

        owner = resolved;

        item = owner.InsertVariableSnapshot(key, value ?? string.Empty, enabled: true, out var insertIndex);
        if (undoRecorder is not null && !undoRecorder.IsApplyingHistory)
        {
            undoRecorder.Record(new CompositeUndoAction(
                $"Define variable '{key}'",
                [new RemoveVariableRowAction(owner, item, insertIndex, $"Remove variable '{key}'")],
                selectAfter: [owner],
                selectAfterInverse: from is not null ? [from] : [owner]));
        }

        return true;
    }
}
