using System.Collections.ObjectModel;
using RequestExplorer.Models;

namespace RequestExplorer.ViewModels;

/// <summary>
/// Keeps exactly one blank "spare" row at the end of a key/value list.
/// Fully blank rows (no key and no value) are collapsed to a single trailing spare.
/// </summary>
internal static class KeyValueSpareRowHelper
{
    public static void EnsureTrailingSpare(
        ObservableCollection<KeyValueItemViewModel> viewModels,
        List<KeyValueItem> modelList,
        Func<KeyValueItem, KeyValueItemViewModel> createVm,
        ref bool ensuringSpare)
    {
        if (ensuringSpare)
        {
            return;
        }

        ensuringSpare = true;
        try
        {
            // Drop duplicate / mid-list fully blank rows (the editable trailing spare).
            // Rows with a value but no key are left alone.
            for (var i = viewModels.Count - 1; i >= 0; i--)
            {
                var vm = viewModels[i];
                if (!IsFullyBlank(vm))
                {
                    continue;
                }

                modelList.Remove(vm.Model);
                viewModels.RemoveAt(i);
            }

            if (viewModels.Count > 0 && string.IsNullOrWhiteSpace(viewModels[^1].Key))
            {
                // Trailing blank-key row already present (spare or value-only).
                return;
            }

            var item = new KeyValueItem { IsEnabled = false };
            modelList.Add(item);
            viewModels.Add(createVm(item));
        }
        finally
        {
            ensuringSpare = false;
        }
    }

    public static bool HasValidTrailingSpare(IReadOnlyList<KeyValueItemViewModel> viewModels)
    {
        if (viewModels.Count == 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(viewModels[^1].Key))
        {
            return false;
        }

        for (var i = 0; i < viewModels.Count - 1; i++)
        {
            if (IsFullyBlank(viewModels[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFullyBlank(KeyValueItemViewModel vm) =>
        string.IsNullOrWhiteSpace(vm.Key) && string.IsNullOrWhiteSpace(vm.Value);
}
