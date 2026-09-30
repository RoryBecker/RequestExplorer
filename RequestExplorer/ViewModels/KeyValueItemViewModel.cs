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
        _key = model.Key;
        _value = model.Value;

        // Blank / spare rows stay disabled until they have a name.
        var enabled = model.IsEnabled && !string.IsNullOrWhiteSpace(model.Key);
        _isEnabled = enabled;
        if (model.IsEnabled != enabled)
        {
            model.IsEnabled = enabled;
        }
    }

    public KeyValueItem Model { get; }

    public bool HasContent =>
        !string.IsNullOrWhiteSpace(Key) || !string.IsNullOrWhiteSpace(Value);

    /// <summary>True when the row has a non-empty key/name and may be toggled on.</summary>
    public bool HasKey => !string.IsNullOrWhiteSpace(Key);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    [NotifyPropertyChangedFor(nameof(HasKey))]
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
                    IsEnabled = value is true && HasKey;
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
        if (value && !HasKey)
        {
            SetEnabledCore(false);
            return;
        }

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

        SyncEnabledWithKey(previous, value);

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

    private void SyncEnabledWithKey(string previousKey, string newKey)
    {
        var hadKey = !string.IsNullOrWhiteSpace(previousKey);
        var hasKey = !string.IsNullOrWhiteSpace(newKey);

        if (!hasKey && IsEnabled)
        {
            // Clearing the name turns the row off without a separate undo step.
            SetEnabledCore(false);
        }
        else if (hasKey && !hadKey && !IsEnabled)
        {
            // Naming a blank spare row turns it on.
            SetEnabledCore(true);
        }
    }

    private void SetEnabledCore(bool enabled)
    {
        if (IsEnabled == enabled && Model.IsEnabled == enabled)
        {
            return;
        }

        var previousSilent = _silent;
        _silent = true;
        try
        {
            IsEnabled = enabled;
            Model.IsEnabled = enabled;
        }
        finally
        {
            _silent = previousSilent;
        }
    }
}
