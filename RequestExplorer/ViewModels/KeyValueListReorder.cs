using System.Collections.ObjectModel;

namespace RequestExplorer.ViewModels;

/// <summary>
/// Computes a new natural order for key/value rows, keeping the trailing spare last.
/// </summary>
internal static class KeyValueListReorder
{
    public static int LastMovableIndex(IList<KeyValueItemViewModel> list)
    {
        if (list.Count == 0)
        {
            return -1;
        }

        var last = list.Count - 1;
        if (IsFullyBlank(list[last]))
        {
            return last - 1;
        }

        return last;
    }

    public static bool SameOrder(
        IList<KeyValueItemViewModel> list,
        IReadOnlyList<KeyValueItemViewModel> order)
    {
        if (list.Count != order.Count)
        {
            return false;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (!ReferenceEquals(list[i], order[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static void ApplyOrder(
        ObservableCollection<KeyValueItemViewModel> list,
        IReadOnlyList<KeyValueItemViewModel> order)
    {
        for (var i = 0; i < order.Count && i < list.Count; i++)
        {
            var current = list.IndexOf(order[i]);
            if (current >= 0 && current != i)
            {
                list.Move(current, i);
            }
        }
    }

    /// <summary>
    /// Moves selected rows one slot up (<paramref name="delta"/> = -1) or down (+1),
    /// skipping the trailing spare. Selected rows never swap with each other.
    /// </summary>
    public static List<KeyValueItemViewModel>? TryCreateNudgedOrder(
        IList<KeyValueItemViewModel> list,
        IReadOnlyList<KeyValueItemViewModel> selected,
        int delta)
    {
        if (delta is not (-1 or 1))
        {
            return null;
        }

        var lastMovable = LastMovableIndex(list);
        if (lastMovable < 0)
        {
            return null;
        }

        var moving = selected
            .Where(item => IsMovable(list, item, lastMovable))
            .Distinct()
            .ToList();
        if (moving.Count == 0)
        {
            return null;
        }

        var movingSet = moving.ToHashSet();
        var canMove = new List<KeyValueItemViewModel>();
        foreach (var item in moving)
        {
            var from = list.IndexOf(item);
            var to = from + delta;
            if (to < 0 || to > lastMovable)
            {
                continue;
            }

            if (movingSet.Contains(list[to]))
            {
                continue;
            }

            canMove.Add(item);
        }

        if (canMove.Count == 0)
        {
            return null;
        }

        var order = list.ToList();
        var process = delta < 0
            ? canMove.OrderBy(order.IndexOf)
            : canMove.OrderByDescending(order.IndexOf);

        foreach (var item in process)
        {
            var from = order.IndexOf(item);
            var to = from + delta;
            if (to < 0 || to >= order.Count)
            {
                continue;
            }

            order.RemoveAt(from);
            order.Insert(to, item);
        }

        return SameOrder(list, order) ? null : order;
    }

    /// <summary>
    /// Places <paramref name="moving"/> as a contiguous block inserted before
    /// <paramref name="insertBefore"/>, still in front of the trailing spare.
    /// </summary>
    public static List<KeyValueItemViewModel>? TryCreateRelocatedOrder(
        IList<KeyValueItemViewModel> list,
        IReadOnlyList<KeyValueItemViewModel> moving,
        int insertBefore)
    {
        var lastMovable = LastMovableIndex(list);
        if (lastMovable < 0)
        {
            return null;
        }

        var movers = moving
            .Where(item => IsMovable(list, item, lastMovable))
            .Distinct()
            .OrderBy(list.IndexOf)
            .ToList();
        if (movers.Count == 0)
        {
            return null;
        }

        insertBefore = Math.Clamp(insertBefore, 0, lastMovable + 1);

        var dest = insertBefore;
        foreach (var mover in movers)
        {
            if (list.IndexOf(mover) < insertBefore)
            {
                dest--;
            }
        }

        var start = list.IndexOf(movers[0]);
        var alreadyThere = dest == start
            && movers.Select((item, i) => list.IndexOf(item) == start + i).All(x => x);
        if (alreadyThere)
        {
            return null;
        }

        var order = list.ToList();
        foreach (var mover in movers)
        {
            order.Remove(mover);
        }

        dest = Math.Clamp(dest, 0, LastMovableIndex(order) + 1);
        dest = Math.Clamp(dest, 0, order.Count);
        for (var i = 0; i < movers.Count; i++)
        {
            order.Insert(dest + i, movers[i]);
        }

        return SameOrder(list, order) ? null : order;
    }

    private static bool IsMovable(
        IList<KeyValueItemViewModel> list,
        KeyValueItemViewModel item,
        int lastMovable)
    {
        var index = list.IndexOf(item);
        return index >= 0 && index <= lastMovable;
    }

    private static bool IsFullyBlank(KeyValueItemViewModel item) =>
        string.IsNullOrWhiteSpace(item.Key) && string.IsNullOrWhiteSpace(item.Value);
}
