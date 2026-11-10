using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.ViewModels;

/// <summary>Read-only ancestor header shown above the local Headers grid.</summary>
public sealed partial class InheritedHeaderRow : ObservableObject
{
    public InheritedHeaderRow(
        string key,
        string sourceName,
        bool isShadowed,
        ContainerNodeViewModel sourceContainer,
        KeyValueItemViewModel sourceItem)
    {
        Key = key;
        SourceName = sourceName;
        IsShadowed = isShadowed;
        SourceContainer = sourceContainer;
        SourceItem = sourceItem;
        SourceItem.PropertyChanged += OnSourceItemPropertyChanged;
    }

    public string Key { get; }

    public string HeaderValue =>
        SourceItem.Value ?? SourceItem.Model.Value ?? string.Empty;

    public string SourceName { get; }

    public bool IsShadowed { get; }

    public ContainerNodeViewModel SourceContainer { get; }

    public KeyValueItemViewModel SourceItem { get; }

    public string StatusDisplay => IsShadowed ? "Overridden" : string.Empty;

    private void OnSourceItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(KeyValueItemViewModel.Value)
            or nameof(KeyValueItemViewModel.Key))
        {
            OnPropertyChanged(nameof(HeaderValue));
        }
    }
}
