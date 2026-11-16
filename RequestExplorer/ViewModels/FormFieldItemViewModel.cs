using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.Models;
using RequestExplorer.Services.Undo;

namespace RequestExplorer.ViewModels;

public partial class FormFieldItemViewModel : ObservableObject
{
    private readonly Action<FormFieldItemViewModel, FormFieldField, object?, object?> _onFieldChanged;
    private bool _silent;

    public FormFieldItemViewModel(
        FormFieldItem model,
        Action<FormFieldItemViewModel, FormFieldField, object?, object?> onFieldChanged)
    {
        Model = model;
        _onFieldChanged = onFieldChanged;
        _key = model.Key;
        _value = model.Value;
        _type = model.Type;
        _fileName = model.FileName;
        _base64Content = model.Base64Content;

        var hasKey = !string.IsNullOrWhiteSpace(model.Key);
        var enabled = model.IsEnabled && hasKey;
        _isEnabled = enabled;
        if (model.IsEnabled != enabled)
        {
            model.IsEnabled = enabled;
        }
    }

    public FormFieldItem Model { get; }

    public bool HasKey => !string.IsNullOrWhiteSpace(Key);

    public bool IsTextType => Type == FormFieldType.Text;

    public bool IsFileType => Type == FormFieldType.File;

    public string FileDisplay =>
        string.IsNullOrWhiteSpace(FileName)
            ? (HasBase64 ? "(file)" : "Choose file…")
            : FileName;

    public bool HasBase64 => !string.IsNullOrWhiteSpace(Base64Content);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKey))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKey))]
    private string _key = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextType))]
    [NotifyPropertyChangedFor(nameof(IsFileType))]
    [NotifyPropertyChangedFor(nameof(FileDisplay))]
    private FormFieldType _type = FormFieldType.Text;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileDisplay))]
    private string _fileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBase64))]
    [NotifyPropertyChangedFor(nameof(FileDisplay))]
    private string _base64Content = string.Empty;

    public object? GetField(FormFieldField field) =>
        field switch
        {
            FormFieldField.IsEnabled => IsEnabled,
            FormFieldField.Key => Key,
            FormFieldField.Type => Type,
            FormFieldField.Value => Value,
            FormFieldField.FileName => FileName,
            FormFieldField.Base64Content => Base64Content,
            _ => null
        };

    public void SetFieldSilent(FormFieldField field, object? value)
    {
        _silent = true;
        try
        {
            switch (field)
            {
                case FormFieldField.IsEnabled:
                    IsEnabled = value is true && HasKey;
                    break;
                case FormFieldField.Key:
                    Key = value as string ?? string.Empty;
                    break;
                case FormFieldField.Type:
                    Type = value is FormFieldType t
                        ? t
                        : Enum.TryParse<FormFieldType>(value?.ToString(), true, out var parsed)
                            ? parsed
                            : FormFieldType.Text;
                    break;
                case FormFieldField.Value:
                    Value = value as string ?? string.Empty;
                    break;
                case FormFieldField.FileName:
                    FileName = value as string ?? string.Empty;
                    break;
                case FormFieldField.Base64Content:
                    Base64Content = value as string ?? string.Empty;
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
            if (_isEnabled)
            {
                _isEnabled = false;
                OnPropertyChanged(nameof(IsEnabled));
            }

            Model.IsEnabled = false;
            return;
        }

        var previous = Model.IsEnabled;
        Model.IsEnabled = value;
        if (!_silent && previous != value)
        {
            _onFieldChanged(this, FormFieldField.IsEnabled, previous, value);
        }
    }

    partial void OnKeyChanged(string value)
    {
        value ??= string.Empty;
        var previous = Model.Key ?? string.Empty;
        Model.Key = value;
        if (string.IsNullOrWhiteSpace(value))
        {
            if (IsEnabled)
            {
                SetFieldSilent(FormFieldField.IsEnabled, false);
            }
        }
        else if (!IsEnabled && !_silent)
        {
            // Naming a spare row turns it on, matching other key/value grids.
            SetFieldSilent(FormFieldField.IsEnabled, true);
            Model.IsEnabled = true;
            _isEnabled = true;
            OnPropertyChanged(nameof(IsEnabled));
        }

        if (!_silent && !string.Equals(previous, value, StringComparison.Ordinal))
        {
            _onFieldChanged(this, FormFieldField.Key, previous, value);
        }
    }

    partial void OnTypeChanged(FormFieldType value)
    {
        var previous = Model.Type;
        Model.Type = value;
        if (_silent || previous == value)
        {
            return;
        }

        // Clear the other mode's payload without extra undo noise; Type change is the record.
        if (value == FormFieldType.Text)
        {
            SetFieldSilent(FormFieldField.FileName, string.Empty);
            SetFieldSilent(FormFieldField.Base64Content, string.Empty);
            Model.FileName = string.Empty;
            Model.Base64Content = string.Empty;
        }
        else
        {
            SetFieldSilent(FormFieldField.Value, string.Empty);
            Model.Value = string.Empty;
        }

        _onFieldChanged(this, FormFieldField.Type, previous, value);
    }

    partial void OnValueChanged(string value)
    {
        value ??= string.Empty;
        var previous = Model.Value ?? string.Empty;
        Model.Value = value;
        if (!_silent && !string.Equals(previous, value, StringComparison.Ordinal))
        {
            _onFieldChanged(this, FormFieldField.Value, previous, value);
        }
    }

    partial void OnFileNameChanged(string value)
    {
        value ??= string.Empty;
        var previous = Model.FileName ?? string.Empty;
        Model.FileName = value;
        if (!_silent && !string.Equals(previous, value, StringComparison.Ordinal))
        {
            _onFieldChanged(this, FormFieldField.FileName, previous, value);
        }
    }

    partial void OnBase64ContentChanged(string value)
    {
        value ??= string.Empty;
        var previous = Model.Base64Content ?? string.Empty;
        Model.Base64Content = value;
        if (!_silent && !string.Equals(previous, value, StringComparison.Ordinal))
        {
            _onFieldChanged(this, FormFieldField.Base64Content, previous, value);
        }
    }

    public void ApplyFile(string fileName, string base64Content)
    {
        var previousName = FileName;
        var previousBytes = Base64Content;
        _silent = true;
        try
        {
            Type = FormFieldType.File;
            Model.Type = FormFieldType.File;
            FileName = fileName ?? string.Empty;
            Model.FileName = FileName;
            Base64Content = base64Content ?? string.Empty;
            Model.Base64Content = Base64Content;
            Value = string.Empty;
            Model.Value = string.Empty;
        }
        finally
        {
            _silent = false;
        }

        _onFieldChanged(this, FormFieldField.FileName, previousName, FileName);
        _onFieldChanged(this, FormFieldField.Base64Content, previousBytes, Base64Content);
        OnPropertyChanged(nameof(FileDisplay));
        OnPropertyChanged(nameof(IsFileType));
        OnPropertyChanged(nameof(IsTextType));
    }
}
