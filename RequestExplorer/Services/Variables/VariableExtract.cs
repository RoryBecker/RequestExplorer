using RequestExplorer.Services.Undo;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

/// <summary>Promotes selected request text into a parent-scoped <c>{{variable}}</c>.</summary>
public static class VariableExtract
{
    /// <summary>
    /// Adds <paramref name="variableName"/> on the request's immediate parent and replaces
    /// the selection with <c>{{name}}</c>. Does not overwrite an existing variable.
    /// </summary>
    public static VariableExtractResult TryExtractSelectedText(
        RequestNodeViewModel request,
        string selectedText,
        int selectionStart,
        int selectionLength,
        string variableName,
        IUndoRecorder? undoRecorder,
        out string? parentName)
    {
        parentName = null;

        if (string.IsNullOrEmpty(selectedText) || selectionLength <= 0 || selectionStart < 0)
        {
            return VariableExtractResult.InvalidSelection;
        }

        if (request.Parent is not ContainerNodeViewModel parent)
        {
            return VariableExtractResult.NoParent;
        }

        parentName = parent.Name;

        var address = request.Address ?? string.Empty;
        if (selectionStart + selectionLength > address.Length)
        {
            return VariableExtractResult.InvalidSelection;
        }

        var key = variableName.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return VariableExtractResult.EmptyName;
        }

        if (key.Contains("{{", StringComparison.Ordinal) || key.Contains("}}", StringComparison.Ordinal))
        {
            return VariableExtractResult.InvalidName;
        }

        if (parent.FindVariableByKey(key) is not null)
        {
            return VariableExtractResult.DuplicateName;
        }

        var token = "{{" + key + "}}";
        var newAddress = string.Concat(
            address.AsSpan(0, selectionStart),
            token,
            address.AsSpan(selectionStart + selectionLength));

        var inserted = parent.InsertVariableSnapshot(key, selectedText, enabled: true, out var insertIndex);
        request.ApplyAddress(newAddress);

        if (undoRecorder is not null && !undoRecorder.IsApplyingHistory)
        {
            undoRecorder.Record(new CompositeUndoAction(
                $"Extract '{key}' as variable",
                [
                    new RemoveVariableRowAction(parent, inserted, insertIndex, $"Remove variable '{key}'"),
                    new SetRequestAddressAction(request, address, "Restore address")
                ],
                selectAfter: [request],
                selectAfterInverse: [request]));
        }

        return VariableExtractResult.Success;
    }

    public static string SuggestVariableName(string selectedText)
    {
        var text = selectedText.Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && !string.IsNullOrEmpty(uri.Host))
        {
            text = uri.Host;
        }

        Span<char> buffer = stackalloc char[Math.Min(text.Length, 48)];
        var length = 0;
        var pendingSeparator = false;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSeparator && length > 0 && length < buffer.Length)
                {
                    buffer[length++] = '_';
                    pendingSeparator = false;
                }

                if (length >= buffer.Length)
                {
                    break;
                }

                buffer[length++] = ch;
            }
            else if (length > 0)
            {
                pendingSeparator = true;
            }
        }

        return length == 0 ? string.Empty : new string(buffer[..length]).Trim('_');
    }
}
