using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RequestExplorer.Models;
using RequestExplorer.Services.KeyValue;
using RequestExplorer.Services.Undo;
using RequestExplorer.ViewModels;

namespace RequestExplorer.ViewModels.Nodes;

/// <summary>Collection or folder that can define scoped key/value variables.</summary>
public abstract partial class ContainerNodeViewModel : TreeNodeViewModel, IKeyValueListSortHost
{
    private const int TooltipMaxLines = 20;
    private const int TooltipValueMaxChars = 64;

    private bool _ensuringSpare;
    private IUndoRecorder? _undoRecorder;

    protected bool SuppressFieldUndo;

    protected ContainerNodeViewModel(string name, List<KeyValueItem> modelVariables, string? description = null)
        : base(name)
    {
        _description = description ?? string.Empty;
        ModelVariables = modelVariables;
        foreach (var item in modelVariables)
        {
            Variables.Add(CreateVariableVm(item));
        }

        EnsureVariableSpareRow();
        Variables.CollectionChanged += Variables_OnCollectionChanged;
        foreach (var item in Variables)
        {
            item.PropertyChanged += VariableItem_OnPropertyChanged;
        }

        PropertyChanged += Self_OnPropertyChanged;
        RefreshVariablePresentation();
    }

    protected List<KeyValueItem> ModelVariables { get; }

    public ObservableCollection<KeyValueItemViewModel> Variables { get; } = [];

    public ObservableCollection<InheritedVariableRow> InheritedVariables { get; } = [];

    [ObservableProperty]
    private bool _hasDefinedVariables;

    [ObservableProperty]
    private bool _hasInheritedVariables;

    [ObservableProperty]
    private string _variablesTooltip = string.Empty;

    /// <summary>Selected row in the Local Variables grid (also used by Go to Definition).</summary>
    [ObservableProperty]
    private KeyValueItemViewModel? _selectedVariable;

    [ObservableProperty]
    private string _description = string.Empty;

    public void AttachUndoRecorder(IUndoRecorder recorder) => _undoRecorder = recorder;

    public IUndoRecorder? UndoRecorder => _undoRecorder;

    /// <summary>Sets description without recording a per-keystroke undo (caller records as needed).</summary>
    public void ApplyDescription(string value)
    {
        SuppressFieldUndo = true;
        try
        {
            Description = value ?? string.Empty;
        }
        finally
        {
            SuppressFieldUndo = false;
        }
    }

    partial void OnDescriptionChanged(string value)
    {
        var normalized = value ?? string.Empty;
        var previous = ReadModelDescription() ?? string.Empty;
        if (string.Equals(previous, normalized, StringComparison.Ordinal))
        {
            return;
        }

        WriteModelDescription(string.IsNullOrEmpty(normalized) ? null : normalized);
        if (_undoRecorder?.IsApplyingHistory != true)
        {
            IsDirty = true;
        }

        RecordDescriptionUndo(previous);
    }

    protected abstract string? ReadModelDescription();

    protected abstract void WriteModelDescription(string? value);

    private void RecordDescriptionUndo(string previous)
    {
        if (SuppressFieldUndo || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        _undoRecorder.Record(
            new SetContainerDescriptionAction(this, previous, "Edit description"));
    }

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
        RefreshVariablePresentation();
    }

    public void ApplyVariableField(
        KeyValueItemViewModel item,
        KeyValueField field,
        object? value)
    {
        SuppressFieldUndo = true;
        try
        {
            item.SetFieldSilent(field, value);
            IsDirty = true;
            EnsureVariableSpareRow();
        }
        finally
        {
            SuppressFieldUndo = false;
        }

        RefreshVariablePresentation();
    }

    public void RemoveVariableRow(KeyValueItemViewModel item)
    {
        SuppressFieldUndo = true;
        try
        {
            ModelVariables.Remove(item.Model);
            Variables.Remove(item);
            IsDirty = true;
            EnsureVariableSpareRow();
        }
        finally
        {
            SuppressFieldUndo = false;
        }

        RefreshVariablePresentation();
    }

    public void InsertVariableRow(KeyValueItemViewModel item, int index)
    {
        SuppressFieldUndo = true;
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
            SuppressFieldUndo = false;
        }

        RefreshVariablePresentation();
    }

    public int IndexOfVariable(KeyValueItemViewModel item) => Variables.IndexOf(item);

    public KeyValueItemViewModel? FindVariableByKey(string key) =>
        Variables.FirstOrDefault(v =>
            v.HasKey && string.Equals(v.Key.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Inserts a new variable before the spare row. Returns the created VM and index.</summary>
        public KeyValueItemViewModel InsertVariableSnapshot(
        string key,
        string value,
        bool enabled,
        out int index,
        bool isSecret = false)
    {
        var model = new KeyValueItem
        {
            Key = key,
            Value = value,
            IsEnabled = enabled && !string.IsNullOrWhiteSpace(key),
            IsSecret = isSecret && !string.IsNullOrWhiteSpace(key)
        };
        var vm = CreateVariableVm(model);
        index = GetInsertBeforeSpareIndex(Variables);
        InsertVariableRow(vm, index);
        return vm;
    }

    /// <summary>Pastes rows before the spare row. Returns how many rows were inserted.</summary>
    public int PasteVariableRows(IReadOnlyList<KeyValueClipboardRow> rows, string operation = "Paste")
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        var undoRemoves = new List<IUndoAction>();
        var inserted = 0;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Key) && string.IsNullOrWhiteSpace(row.Value))
            {
                continue;
            }

            var model = new KeyValueItem
            {
                Key = row.Key ?? string.Empty,
                Value = row.Value ?? string.Empty,
                IsEnabled = row.IsEnabled && !string.IsNullOrWhiteSpace(row.Key),
                IsSecret = row.IsSecret && !string.IsNullOrWhiteSpace(row.Key)
            };
            var vm = CreateVariableVm(model);
            var index = GetInsertBeforeSpareIndex(Variables);
            InsertVariableRow(vm, index);
            undoRemoves.Add(new RemoveVariableRowAction(this, vm, index, $"{operation} variable '{vm.Key}'"));
            inserted++;
        }

        if (inserted == 0)
        {
            return 0;
        }

        undoRemoves.Reverse();
        var description = inserted == 1
            ? $"{operation} variable row"
            : $"{operation} {inserted} variable rows";
        if (_undoRecorder is not null && !_undoRecorder.IsApplyingHistory)
        {
            _undoRecorder.Record(new CompositeUndoAction(description, undoRemoves));
        }

        return inserted;
    }

    private static int GetInsertBeforeSpareIndex(ObservableCollection<KeyValueItemViewModel> list) =>
        list.Count > 0 && string.IsNullOrWhiteSpace(list[^1].Key)
            ? list.Count - 1
            : list.Count;

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
            RefreshVariablePresentation();
        }
    }

    public void RefreshVariablePresentation()
    {
        RefreshDefinedVariablesGlyph();
        RefreshInheritedVariables();
        NotifyDescendantResolvedAddresses();
        RefreshDescendantInheritedVariables();
    }

    private void NotifyDescendantResolvedAddresses()
    {
        foreach (var child in Children)
        {
            switch (child)
            {
                case RequestNodeViewModel request:
                    request.NotifyResolvedAddressChanged();
                    break;
                case ContainerNodeViewModel container:
                    container.NotifyDescendantResolvedAddresses();
                    break;
            }
        }
    }

    private void RefreshDescendantInheritedVariables()
    {
        foreach (var child in Children)
        {
            if (child is ContainerNodeViewModel container)
            {
                container.RefreshInheritedVariables();
                container.RefreshDescendantInheritedVariables();
            }
        }
    }

    public void RefreshInheritedVariables()
    {
        InheritedVariables.Clear();

        // Only enabled locals shadow ancestors — disabled rows stay out of the chain.
        var localKeys = new HashSet<string>(
            Variables.Where(v => v.IsEnabled && v.HasKey).Select(v => v.Key.Trim()),
            StringComparer.OrdinalIgnoreCase);

        // Nearest ancestor wins (same as VariableResolver.BuildScope) — one row per key.
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<InheritedVariableRow>();

        for (var node = Parent; node is not null; node = node.Parent)
        {
            if (node is not ContainerNodeViewModel ancestor)
            {
                continue;
            }

            foreach (var variable in ancestor.Variables)
            {
                if (!variable.IsEnabled || !variable.HasKey)
                {
                    continue;
                }

                var key = variable.Key.Trim();
                if (!seenKeys.Add(key))
                {
                    continue;
                }

                rows.Add(new InheritedVariableRow(
                    key,
                    ancestor.Name,
                    localKeys.Contains(key),
                    ancestor,
                    variable));
            }
        }

        // Nearest ancestor first, each ancestor in its own list order (not A–Z),
        // so a parent reorder shows up on child Inherited grids.
        foreach (var row in rows)
        {
            InheritedVariables.Add(row);
        }

        HasInheritedVariables = InheritedVariables.Count > 0;
    }

    private void RefreshDefinedVariablesGlyph()
    {
        var defined = Variables
            .Where(v => v.IsEnabled && v.HasKey)
            .OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        HasDefinedVariables = defined.Count > 0;

        if (defined.Count == 0)
        {
            VariablesTooltip = string.Empty;
            return;
        }

        var sb = new StringBuilder();
        var lines = 0;
        foreach (var item in defined)
        {
            if (lines >= TooltipMaxLines)
            {
                sb.AppendLine("…");
                break;
            }

            var value = item.Value ?? string.Empty;
            if (value.Length > TooltipValueMaxChars)
            {
                value = value[..TooltipValueMaxChars] + "…";
            }

            sb.Append(item.Key.Trim());
            sb.Append('=');
            sb.Append(value);
            sb.AppendLine();
            lines++;
        }

        VariablesTooltip = sb.ToString().TrimEnd();
    }

    public KeyValueItemViewModel CreateBoundVariableItem(KeyValueItem item) =>
        CreateVariableVm(item);

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
        RefreshVariablePresentation();
    }

    private void RecordFieldUndo(
        KeyValueItemViewModel item,
        KeyValueField field,
        object? oldValue,
        object? newValue)
    {
        if (SuppressFieldUndo || _ensuringSpare
            || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        var description = field switch
        {
            KeyValueField.IsEnabled => $"Toggle variable '{item.Key}'",
            KeyValueField.Key => "Edit variable key",
            KeyValueField.Value => "Edit variable value",
            KeyValueField.IsSecret => $"Toggle secret '{item.Key}'",
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
        if (SuppressFieldUndo || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
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

    /// <summary>Ensures variables have a single trailing blank spare row.</summary>
    public void NormalizeSpareRows() => EnsureVariableSpareRow();

    public void OnKeyValueListReordered(ObservableCollection<KeyValueItemViewModel> list)
    {
        if (!ReferenceEquals(list, Variables))
        {
            return;
        }

        ModelVariables.Clear();
        foreach (var vm in Variables)
        {
            ModelVariables.Add(vm.Model);
        }

        IsDirty = true;
        EnsureVariableSpareRow();
        RefreshVariablePresentation();
    }

    public bool TryApplyKeyValueOrder(
        ObservableCollection<KeyValueItemViewModel> list,
        IReadOnlyList<KeyValueItemViewModel> newOrder)
    {
        if (!ReferenceEquals(list, Variables) || KeyValueListReorder.SameOrder(list, newOrder))
        {
            return false;
        }

        var previous = list.ToArray();
        KeyValueListReorder.ApplyOrder(list, newOrder);
        OnKeyValueListReordered(list);

        if (!SuppressFieldUndo && _undoRecorder is not null && !_undoRecorder.IsApplyingHistory)
        {
            _undoRecorder.Record(new ReorderVariableListAction(this, previous, "Reorder variables"));
        }

        return true;
    }

    private void EnsureVariableSpareRow()
    {
        if (!_ensuringSpare && KeyValueSpareRowHelper.HasValidTrailingSpare(Variables))
        {
            return;
        }

        KeyValueSpareRowHelper.EnsureTrailingSpare(
            Variables,
            ModelVariables,
            CreateVariableVm,
            ref _ensuringSpare);
    }

    private void Variables_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (KeyValueItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= VariableItem_OnPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (KeyValueItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += VariableItem_OnPropertyChanged;
            }
        }

        if (e.Action != NotifyCollectionChangedAction.Move)
        {
            RefreshVariablePresentation();
        }
    }

    private void VariableItem_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(KeyValueItemViewModel.Key)
            or nameof(KeyValueItemViewModel.Value)
            or nameof(KeyValueItemViewModel.IsEnabled)
            or nameof(KeyValueItemViewModel.IsSecret)
            or nameof(KeyValueItemViewModel.HasKey))
        {
            RefreshVariablePresentation();
        }
    }

    private void Self_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Parent) or nameof(Name))
        {
            RefreshInheritedVariables();
        }
    }
}
