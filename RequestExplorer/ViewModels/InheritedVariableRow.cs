using CommunityToolkit.Mvvm.ComponentModel;

namespace RequestExplorer.ViewModels;

/// <summary>Read-only ancestor variable shown above the local Variables grid.</summary>
public sealed partial class InheritedVariableRow : ObservableObject
{
    public InheritedVariableRow(
        string key,
        string value,
        string sourceName,
        bool isShadowed,
        ContainerNodeViewModel sourceContainer)
    {
        Key = key;
        Value = value;
        SourceName = sourceName;
        IsShadowed = isShadowed;
        SourceContainer = sourceContainer;
    }

    public string Key { get; }

    public string Value { get; }

    public string SourceName { get; }

    public bool IsShadowed { get; }

    /// <summary>Collection or folder that defines this variable.</summary>
    public ContainerNodeViewModel SourceContainer { get; }

    public string StatusDisplay => IsShadowed ? "Overridden" : string.Empty;
}
