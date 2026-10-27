using System.Collections.ObjectModel;

namespace RequestExplorer.ViewModels;

/// <summary>Implemented by request/container editors so grid sort/reorder can sync model lists.</summary>
public interface IKeyValueListSortHost
{
    void OnKeyValueListReordered(ObservableCollection<KeyValueItemViewModel> list);

    /// <summary>
    /// Applies <paramref name="newOrder"/> to an owned list, syncs the model, and records undo.
    /// Returns false when the list is not owned or the order is unchanged.
    /// </summary>
    bool TryApplyKeyValueOrder(
        ObservableCollection<KeyValueItemViewModel> list,
        IReadOnlyList<KeyValueItemViewModel> newOrder);
}
