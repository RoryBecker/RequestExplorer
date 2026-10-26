using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

/// <summary>Finds the collection/folder row that defines a <c>{{name}}</c> token.</summary>
public static class VariableDefinitionLookup
{
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
}
