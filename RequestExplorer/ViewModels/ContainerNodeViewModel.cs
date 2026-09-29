using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using RequestExplorer.Models;
using RequestExplorer.Services.Undo;

namespace RequestExplorer.ViewModels;

/// <summary>Collection or folder that can define scoped key/value variables.</summary>
public abstract partial class ContainerNodeViewModel : TreeNodeViewModel
{
    private bool _ensuringSpare;
    private bool _suppressFieldUndo;
    private IUndoRecorder? _undoRecorder;

    protected ContainerNodeViewModel(string name, List<KeyValueItem> modelVariables)
        : base(name)
    {
        ModelVariables = modelVariables;
        foreach (var item in modelVariables)
        {
            Variables.Add(CreateVariableVm(item));
        }

        EnsureVariableSpareRow();
    }

    protected List<KeyValueItem> ModelVariables { get; }

    public ObservableCollection<KeyValueItemViewModel> Variables { get; } = [];

    public void AttachUndoRecorder(IUndoRecorder recorder) => _undoRecorder = recorder;

    [RelayCommand]
    private void RemoveVariable(KeyValueItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Key) && string.IsNullOrWhiteSpace(item.Value)
            && Variables.Count == 1)
        {
            return;
        }

        var index = Variables.IndexOf(item);
        ModelVariables.Remove(item.Model);
        Variables.Remove(item);
        IsDirty = true;
        EnsureVariableSpareRow();
        RecordRowRemoveUndo(item, index, $"Remove variable '{item.Key}'");
    }

    public void ApplyVariableField(
        KeyValueItemViewModel item,
        KeyValueField field,
        object? value)
    {
        _suppressFieldUndo = true;
        try
        {
            item.SetFieldSilent(field, value);
            IsDirty = true;
            EnsureVariableSpareRow();
        }
        finally
        {
            _suppressFieldUndo = false;
        }
    }

    public void RemoveVariableRow(KeyValueItemViewModel item)
    {
        _suppressFieldUndo = true;
        try
        {
            ModelVariables.Remove(item.Model);
            Variables.Remove(item);
            IsDirty = true;
            EnsureVariableSpareRow();
        }
        finally
        {
            _suppressFieldUndo = false;
        }
    }

    public void InsertVariableRow(KeyValueItemViewModel item, int index)
    {
        _suppressFieldUndo = true;
        try
        {
            if (Variables.Contains(item))
            {
                return;
            }

            index = Math.Clamp(index, 0, Variables.Count);
            if (!ModelVariables.Contains(item.Model))
            {
                var modelIndex = Math.Clamp(index, 0, ModelVariables.Count);
                ModelVariables.Insert(modelIndex, item.Model);
            }

            Variables.Insert(index, item);
            IsDirty = true;
            EnsureVariableSpareRow();
        }
        finally
        {
            _suppressFieldUndo = false;
        }
    }

    public int IndexOfVariable(KeyValueItemViewModel item) => Variables.IndexOf(item);

    /// <summary>Appends imported variables before the spare row and marks dirty.</summary>
    public void AppendVariables(IEnumerable<KeyValueItem> items)
    {
        var added = false;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
            {
                continue;
            }

            // Insert before trailing spare row when present.
            var insertAt = Variables.Count > 0 && string.IsNullOrWhiteSpace(Variables[^1].Key)
                ? Variables.Count - 1
                : Variables.Count;
            var modelIndex = Math.Clamp(insertAt, 0, ModelVariables.Count);
            ModelVariables.Insert(modelIndex, item);
            Variables.Insert(insertAt, CreateVariableVm(item));
            added = true;
        }

        if (added)
        {
            IsDirty = true;
            EnsureVariableSpareRow();
        }
    }

    private KeyValueItemViewModel CreateVariableVm(KeyValueItem item) =>
        new(item, OnVariableFieldChanged);

    private void OnVariableFieldChanged(
        KeyValueItemViewModel item,
        KeyValueField field,
        object? oldValue,
        object? newValue)
    {
        IsDirty = true;
        EnsureVariableSpareRow();
        RecordFieldUndo(item, field, oldValue, newValue);
    }

    private void RecordFieldUndo(
        KeyValueItemViewModel item,
        KeyValueField field,
        object? oldValue,
        object? newValue)
    {
        if (_suppressFieldUndo || _ensuringSpare
            || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        var description = field switch
        {
            KeyValueField.IsEnabled => $"Toggle variable '{item.Key}'",
            KeyValueField.Key => "Edit variable key",
            KeyValueField.Value => "Edit variable value",
            _ => "Edit variable"
        };

        _undoRecorder.Record(
            new SetVariableFieldAction(this, item, field, oldValue, description));
    }

    private void RecordRowRemoveUndo(
        KeyValueItemViewModel item,
        int index,
        string description)
    {
        if (_suppressFieldUndo || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        if (index < 0)
        {
            return;
        }

        _undoRecorder.Record(
            new InsertVariableRowAction(this, item, index, description));
    }

    private void EnsureVariableSpareRow()
    {
        if (_ensuringSpare)
        {
            return;
        }

        if (Variables.Count > 0 && string.IsNullOrWhiteSpace(Variables[^1].Key))
        {
            return;
        }

        _ensuringSpare = true;
        try
        {
            var item = new KeyValueItem();
            ModelVariables.Add(item);
            Variables.Add(CreateVariableVm(item));
        }
        finally
        {
            _ensuringSpare = false;
        }
    }
}
