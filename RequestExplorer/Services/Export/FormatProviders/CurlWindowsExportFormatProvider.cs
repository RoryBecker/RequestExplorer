using RequestExplorer.Models;

namespace RequestExplorer.Services.Export.FormatProviders;

public sealed class CurlWindowsExportFormatProvider : IExportFormatProvider
{
    public RequestExportFormat Format => RequestExportFormat.CurlWindows;

    public string DisplayName => "cURL (Windows)";

    public string Export(RequestDefinition request) =>
        CurlExportFormatProvider.FormatCurl(request, multiline: false);
}
