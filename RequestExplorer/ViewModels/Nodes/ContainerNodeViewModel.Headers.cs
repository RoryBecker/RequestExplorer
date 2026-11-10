using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using RequestExplorer.Models;
using RequestExplorer.Services.KeyValue;
using RequestExplorer.Services.Preferences;
using RequestExplorer.Services.Undo;
using RequestExplorer.ViewModels;

namespace RequestExplorer.ViewModels.Nodes;

public abstract partial class ContainerNodeViewModel
{
    [RelayCommand]
    private void RemoveHeader(KeyValueItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Key) && string.IsNullOrWhiteSpace(item.Value)
            && Headers.Count == 1)
        {
            return;
        }

        var index = Headers.IndexOf(item);
        ModelHeaders.Remove(item.Model);
        Headers.Remove(item);
        IsDirty = true;
        EnsureHeaderSpareRow();
        RecordHeaderRowRemoveUndo(item, index, $"Remove header '{item.Key}'");
        RefreshHeaderPresentation();
    }

    public void ApplyHeaderField(
        KeyValueItemViewModel item,
        KeyValueField field,
        object? value)
    {
        SuppressFieldUndo = true;
        try
        {
            item.SetFieldSilent(field, value);
            IsDirty = true;
            EnsureHeaderSpareRow();
        }
        finally
        {
            SuppressFieldUndo = false;
        }

        RefreshHeaderPresentation();
    }

    public void RemoveHeaderRow(KeyValueItemViewModel item)
    {
        SuppressFieldUndo = true;
        try
        {
            ModelHeaders.Remove(item.Model);
            Headers.Remove(item);
            IsDirty = true;
            EnsureHeaderSpareRow();
        }
        finally
        {
            SuppressFieldUndo = false;
        }

        RefreshHeaderPresentation();
    }

    public void InsertHeaderRow(KeyValueItemViewModel item, int index)
    {
        SuppressFieldUndo = true;
        try
        {
            if (Headers.Contains(item))
            {
                return;
            }

            index = Math.Clamp(index, 0, Headers.Count);
            if (!ModelHeaders.Contains(item.Model))
            {
                var modelIndex = Math.Clamp(index, 0, ModelHeaders.Count);
                ModelHeaders.Insert(modelIndex, item.Model);
            }

            Headers.Insert(index, item);
            IsDirty = true;
            EnsureHeaderSpareRow();
        }
        finally
        {
            SuppressFieldUndo = false;
        }

        RefreshHeaderPresentation();
    }

    public int IndexOfHeader(KeyValueItemViewModel item) => Headers.IndexOf(item);

    public KeyValueItemViewModel? FindHeaderByKey(string key) =>
        Headers.FirstOrDefault(h =>
            h.HasKey && string.Equals(h.Key.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase));

    public KeyValueItemViewModel InsertHeaderSnapshot(
        string key,
        string value,
        bool enabled,
        out int index,
        bool isInheritable = true)
    {
        var model = new KeyValueItem
        {
            Key = key,
            Value = value,
            IsEnabled = enabled && !string.IsNullOrWhiteSpace(key),
            IsInheritable = isInheritable && !string.IsNullOrWhiteSpace(key)
        };
        var vm = CreateHeaderVm(model);
        index = GetInsertBeforeSpareIndex(Headers);
        InsertHeaderRow(vm, index);
        return vm;
    }

    public int PasteHeaderRows(IReadOnlyList<KeyValueClipboardRow> rows, string operation = "Paste")
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
                IsInheritable = row.IsInheritable && !string.IsNullOrWhiteSpace(row.Key)
            };
            var vm = CreateHeaderVm(model);
            var index = GetInsertBeforeSpareIndex(Headers);
            InsertHeaderRow(vm, index);
            undoRemoves.Add(new RemoveHeaderRowAction(this, vm, index, $"{operation} header '{vm.Key}'"));
            inserted++;
        }

        if (inserted == 0)
        {
            return 0;
        }

        undoRemoves.Reverse();
        var description = inserted == 1
            ? $"{operation} header row"
            : $"{operation} {inserted} header rows";
        if (_undoRecorder is not null && !_undoRecorder.IsApplyingHistory)
        {
            _undoRecorder.Record(new CompositeUndoAction(description, undoRemoves));
        }

        return inserted;
    }

    public void RefreshHeaderPresentation()
    {
        RefreshDefinedHeadersGlyph();
        RefreshInheritedHeaders();
        RefreshDescendantInheritedHeaders();
    }

    public void RefreshInheritedHeaders()
    {
        InheritedHeaders.Clear();

        var localKeys = new HashSet<string>(
            Headers.Where(h => h.IsEnabled && h.HasKey).Select(h => h.Key.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<InheritedHeaderRow>();

        for (var node = Parent; node is not null; node = node.Parent)
        {
            if (node is not ContainerNodeViewModel ancestor)
            {
                continue;
            }

            foreach (var header in ancestor.Headers)
            {
                if (!header.IsEnabled || !header.HasKey)
                {
                    continue;
                }

                var key = header.Key.Trim();
                if (!seenKeys.Add(key))
                {
                    continue;
                }

                if (!InheritedGridPreferences.IsEffectivelyInheritable(header.IsInheritable))
                {
                    continue;
                }

                rows.Add(new InheritedHeaderRow(
                    key,
                    ancestor.Name,
                    localKeys.Contains(key),
                    ancestor,
                    header));
            }
        }

        foreach (var row in rows)
        {
            InheritedHeaders.Add(row);
        }

        HasInheritedHeaders = InheritedHeaders.Count > 0;
        OnPropertyChanged(nameof(ShowInheritedHeadersGrid));
    }

    private void RefreshDefinedHeadersGlyph()
    {
        var defined = Headers
            .Where(h => h.IsEnabled && h.HasKey)
            .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        HasDefinedHeaders = defined.Count > 0;

        if (defined.Count == 0)
        {
            HeadersTooltip = string.Empty;
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
            sb.Append(": ");
            sb.Append(value);
            sb.AppendLine();
            lines++;
        }

        HeadersTooltip = sb.ToString().TrimEnd();
    }

    public KeyValueItemViewModel CreateBoundHeaderItem(KeyValueItem item) =>
        CreateHeaderVm(item);

    private KeyValueItemViewModel CreateHeaderVm(KeyValueItem item) =>
        new(item, OnHeaderFieldChanged);

    private void OnHeaderFieldChanged(
        KeyValueItemViewModel item,
        KeyValueField field,
        object? oldValue,
        object? newValue)
    {
        IsDirty = true;
        EnsureHeaderSpareRow();
        RecordHeaderFieldUndo(item, field, oldValue, newValue);
        RefreshHeaderPresentation();
    }

    private void RecordHeaderFieldUndo(
        KeyValueItemViewModel item,
        KeyValueField field,
        object? oldValue,
        object? newValue)
    {
        if (SuppressFieldUndo || _ensuringHeaderSpare
            || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        var description = field switch
        {
            KeyValueField.IsEnabled => $"Toggle header '{item.Key}'",
            KeyValueField.Key => "Edit header key",
            KeyValueField.Value => "Edit header value",
            KeyValueField.IsInheritable => $"Toggle inherit '{item.Key}'",
            _ => "Edit header"
        };

        _undoRecorder.Record(
            new SetHeaderFieldAction(this, item, field, oldValue, description));
    }

    private void RecordHeaderRowRemoveUndo(
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
            new InsertHeaderRowAction(this, item, index, description));
    }

    private void EnsureHeaderSpareRow()
    {
        if (!_ensuringHeaderSpare && KeyValueSpareRowHelper.HasValidTrailingSpare(Headers))
        {
            return;
        }

        KeyValueSpareRowHelper.EnsureTrailingSpare(
            Headers,
            ModelHeaders,
            CreateHeaderVm,
            ref _ensuringHeaderSpare);
    }

    private void Headers_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (KeyValueItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= HeaderItem_OnPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (KeyValueItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += HeaderItem_OnPropertyChanged;
            }
        }

        if (e.Action != NotifyCollectionChangedAction.Move)
        {
            RefreshHeaderPresentation();
        }
    }

    private void HeaderItem_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(KeyValueItemViewModel.Key)
            or nameof(KeyValueItemViewModel.Value)
            or nameof(KeyValueItemViewModel.IsEnabled)
            or nameof(KeyValueItemViewModel.IsInheritable)
            or nameof(KeyValueItemViewModel.HasKey))
        {
            RefreshHeaderPresentation();
        }
    }
}
