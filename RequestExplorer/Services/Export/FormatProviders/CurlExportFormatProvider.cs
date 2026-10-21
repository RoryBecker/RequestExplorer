using System.Text;
using RequestExplorer.Models;

namespace RequestExplorer.Services.Export.FormatProviders;

public sealed class CurlExportFormatProvider : IExportFormatProvider
{
    public RequestExportFormat Format => RequestExportFormat.Curl;

    public string DisplayName => "cURL";

    public string Export(RequestDefinition request) => FormatCurl(request, multiline: true);

    internal static string FormatCurl(RequestDefinition request, bool multiline)
    {
        var sb = new StringBuilder();
        var url = RequestExportSupport.BuildUrl(request);
        var method = request.Verb.ToString().ToUpperInvariant();
        var sep = multiline ? " \\\n  " : " ";

        sb.Append("curl --request ").Append(method);
        sb.Append(sep).Append("--url ").Append(RequestExportSupport.ShellSingleQuote(url));

        foreach (var (name, value) in RequestExportSupport.GetEffectiveHeaders(request))
        {
            sb.Append(sep).Append("--header ").Append(RequestExportSupport.ShellSingleQuote($"{name}: {value}"));
        }

        if (RequestExportSupport.HasBody(request))
        {
            sb.Append(sep).Append("--data-raw ").Append(RequestExportSupport.ShellSingleQuote(request.Body.Content));
        }

        return sb.ToString();
    }
}
