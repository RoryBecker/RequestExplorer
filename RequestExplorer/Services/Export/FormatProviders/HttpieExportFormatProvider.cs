using System.Text;
using RequestExplorer.Models;

namespace RequestExplorer.Services.Export.FormatProviders;

public sealed class HttpieExportFormatProvider : IExportFormatProvider
{
    public RequestExportFormat Format => RequestExportFormat.Httpie;

    public string DisplayName => "HTTPie";

    public string Export(RequestDefinition request)
    {
        var url = RequestExportSupport.BuildUrl(request);
        var method = request.Verb.ToString().ToUpperInvariant();
        var sb = new StringBuilder();
        sb.Append("http ").Append(method).Append(' ').Append(RequestExportSupport.ShellEscapeHttpieUrl(url));

        foreach (var (name, value) in RequestExportSupport.GetEffectiveHeaders(request))
        {
            sb.Append(" \\\n  ").Append(name).Append(':').Append(RequestExportSupport.ShellSingleQuote(value));
        }

        if (RequestExportSupport.HasBody(request))
        {
            // Prefer raw body via stdin-style flag for arbitrary content.
            sb.Append(" \\\n  --raw ").Append(RequestExportSupport.ShellSingleQuote(request.Body.Content));
        }

        return sb.ToString();
    }
}
