using System.Text;
using RequestExplorer.Models;

namespace RequestExplorer.Services.Export.FormatProviders;

public sealed class HttpExportFormatProvider : IExportFormatProvider
{
    public RequestExportFormat Format => RequestExportFormat.Http;

    public string DisplayName => "HTTP";

    public string Export(RequestDefinition request)
    {
        var url = new Uri(RequestExportSupport.BuildUrl(request));
        var pathAndQuery = string.IsNullOrEmpty(url.PathAndQuery) ? "/" : url.PathAndQuery;
        var sb = new StringBuilder();
        sb.Append(request.Verb.ToString().ToUpperInvariant())
          .Append(' ')
          .Append(pathAndQuery)
          .Append(" HTTP/1.1\n");
        sb.Append("Host: ").Append(url.IsDefaultPort ? url.Host : $"{url.Host}:{url.Port}").Append('\n');

        foreach (var (name, value) in RequestExportSupport.GetEffectiveHeaders(request))
        {
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.Append(name).Append(": ").Append(value).Append('\n');
        }

        if (RequestExportSupport.HasBody(request))
        {
            sb.Append('\n').Append(request.Body.Content);
        }

        return sb.ToString();
    }
}
