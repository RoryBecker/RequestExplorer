using RequestExplorer.Models;
using RequestExplorer.Services.Export.FormatProviders;

namespace RequestExplorer.Services.Export;

public static class RequestExportService
{
    private static readonly IExportFormatProvider[] ProviderList =
    [
        new CurlExportFormatProvider(),
        new CurlWindowsExportFormatProvider(),
        new HttpExportFormatProvider(),
        new PostmanCollectionExportFormatProvider(),
        new FetchExportFormatProvider(),
        new PowerShellExportFormatProvider(),
        new PythonRequestsExportFormatProvider(),
        new HttpieExportFormatProvider()
    ];

    private static readonly Dictionary<RequestExportFormat, IExportFormatProvider> ProvidersByFormat =
        ProviderList.ToDictionary(p => p.Format);

    public static IReadOnlyList<RequestExportFormatOption> Formats { get; } =
        ProviderList.Select(p => new RequestExportFormatOption(p.Format, p.DisplayName)).ToArray();

    public static string Export(RequestDefinition request, RequestExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Address))
        {
            return "// No URL on this request yet.";
        }

        var provider = ProvidersByFormat.GetValueOrDefault(format) ?? ProviderList[0];
        return provider.Export(request);
    }
}
