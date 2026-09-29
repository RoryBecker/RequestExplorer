using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.Models;

namespace RequestExplorer.ViewModels;

public partial class KeyValueItemViewModel : ObservableObject
{
    private readonly Action _onChanged;

    public KeyValueItemViewModel(KeyValueItem model, Action onChanged)
    {
        Model = model;
        _onChanged = onChanged;
        _isEnabled = model.IsEnabled;
        _key = model.Key;
        _value = model.Value;
    }

    public KeyValueItem Model { get; }

    public bool HasContent =>
        !string.IsNullOrWhiteSpace(Key) || !string.IsNullOrWhiteSpace(Value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    private string _key;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    private string _value;

    partial void OnIsEnabledChanged(bool value)
    {
        Model.IsEnabled = value;
        _onChanged();
    }

    partial void OnKeyChanged(string value)
    {
        Model.Key = value;
        _onChanged();
    }

    partial void OnValueChanged(string value)
    {
        Model.Value = value;
        _onChanged();
    }
}
