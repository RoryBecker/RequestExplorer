using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.ViewModels;

/// <summary>Read-only ancestor variable shown above the local Variables grid.</summary>
public sealed partial class InheritedVariableRow : ObservableObject
{
    public InheritedVariableRow(
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

    /// <summary>Live value from the defining row (avoids stale snapshots / Path=Value binding issues).</summary>
    public string VariableValue =>
        SourceItem.Value ?? SourceItem.Model.Value ?? string.Empty;

    public string SourceName { get; }

    public bool IsShadowed { get; }

    public bool IsSecret => SourceItem.IsSecret;

    /// <summary>Collection or folder that defines this variable.</summary>
    public ContainerNodeViewModel SourceContainer { get; }

    /// <summary>The defining row in <see cref="SourceContainer"/>'s Variables list.</summary>
    public KeyValueItemViewModel SourceItem { get; }

    public string StatusDisplay => IsShadowed ? "Overridden" : string.Empty;

    private void OnSourceItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(KeyValueItemViewModel.Value)
            or nameof(KeyValueItemViewModel.Key))
        {
            OnPropertyChanged(nameof(VariableValue));
        }

        if (e.PropertyName is nameof(KeyValueItemViewModel.IsSecret))
        {
            OnPropertyChanged(nameof(IsSecret));
        }
    }
}
