using System.Collections.ObjectModel;

namespace RequestExplorer.ViewModels;

/// <summary>Implemented by request/container editors so grid sort can sync model lists.</summary>
public interface IKeyValueListSortHost
{
    void OnKeyValueListReordered(ObservableCollection<KeyValueItemViewModel> list);
}
