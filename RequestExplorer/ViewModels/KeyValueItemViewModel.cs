using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.Models;
using RequestExplorer.Services.Undo;

namespace RequestExplorer.ViewModels;

public partial class KeyValueItemViewModel : ObservableObject
{
    private readonly Action<KeyValueItemViewModel, KeyValueField, object?, object?> _onFieldChanged;
    private bool _silent;

    public KeyValueItemViewModel(
        KeyValueItem model,
        Action<KeyValueItemViewModel, KeyValueField, object?, object?> onFieldChanged)
    {
        Model = model;
        _onFieldChanged = onFieldChanged;
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

    public object? GetField(KeyValueField field) =>
        field switch
        {
            KeyValueField.IsEnabled => IsEnabled,
            KeyValueField.Key => Key,
            KeyValueField.Value => Value,
            _ => null
        };

    public void SetFieldSilent(KeyValueField field, object? value)
    {
        _silent = true;
        try
        {
            switch (field)
            {
                case KeyValueField.IsEnabled:
                    IsEnabled = value is true;
                    break;
                case KeyValueField.Key:
                    Key = value as string ?? string.Empty;
                    break;
                case KeyValueField.Value:
                    Value = value as string ?? string.Empty;
                    break;
            }
        }
        finally
        {
            _silent = false;
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        var previous = Model.IsEnabled;
        Model.IsEnabled = value;
        if (!_silent && previous != value)
        {
            _onFieldChanged(this, KeyValueField.IsEnabled, previous, value);
        }
    }

    partial void OnKeyChanged(string value)
    {
        var previous = Model.Key;
        Model.Key = value;
        if (!_silent && !string.Equals(previous, value, StringComparison.Ordinal))
        {
            _onFieldChanged(this, KeyValueField.Key, previous, value);
        }
    }

    partial void OnValueChanged(string value)
    {
        var previous = Model.Value;
        Model.Value = value;
        if (!_silent && !string.Equals(previous, value, StringComparison.Ordinal))
        {
            _onFieldChanged(this, KeyValueField.Value, previous, value);
        }
    }
}
