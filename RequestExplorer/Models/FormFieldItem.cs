namespace RequestExplorer.Models;

/// <summary>One multipart form-data part (text value or file bytes as base64).</summary>
public sealed class FormFieldItem
{
    public bool IsEnabled { get; set; } = true;

    public string Key { get; set; } = string.Empty;

    public FormFieldType Type { get; set; } = FormFieldType.Text;

    /// <summary>Text part value. Unused for <see cref="FormFieldType.File"/>.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Original file name for file parts (not a path).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Base64 file bytes for file parts.</summary>
    public string Base64Content { get; set; } = string.Empty;
}
