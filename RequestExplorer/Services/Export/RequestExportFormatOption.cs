namespace RequestExplorer.Services.Export;

public sealed class RequestExportFormatOption
{
    public RequestExportFormatOption(RequestExportFormat format, string displayName)
    {
        Format = format;
        DisplayName = displayName;
    }

    public RequestExportFormat Format { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
