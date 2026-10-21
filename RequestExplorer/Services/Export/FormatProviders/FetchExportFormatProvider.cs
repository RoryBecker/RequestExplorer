using System.Text;
using RequestExplorer.Models;

namespace RequestExplorer.Services.Export.FormatProviders;

public sealed class FetchExportFormatProvider : IExportFormatProvider
{
    public RequestExportFormat Format => RequestExportFormat.Fetch;

    public string DisplayName => "JavaScript Fetch";

    public string Export(RequestDefinition request)
    {
        var url = RequestExportSupport.BuildUrl(request);
        var method = request.Verb.ToString().ToUpperInvariant();
        var sb = new StringBuilder();
        sb.Append("fetch(").Append(RequestExportSupport.JsString(url)).Append(", {\n");
        sb.Append("  method: ").Append(RequestExportSupport.JsString(method)).Append(",\n");

        var headers = RequestExportSupport.GetEffectiveHeaders(request).ToList();
        if (headers.Count > 0)
        {
            sb.Append("  headers: {\n");
            for (var i = 0; i < headers.Count; i++)
            {
                var (name, value) = headers[i];
                sb.Append("    ").Append(RequestExportSupport.JsString(name)).Append(": ").Append(RequestExportSupport.JsString(value));
                if (i < headers.Count - 1)
                {
                    sb.Append(',');
                }

                sb.Append('\n');
            }

            sb.Append("  }");
            if (RequestExportSupport.HasBody(request))
            {
                sb.Append(',');
            }

            sb.Append('\n');
        }

        if (RequestExportSupport.HasBody(request))
        {
            sb.Append("  body: ").Append(RequestExportSupport.JsString(request.Body.Content)).Append('\n');
        }

        sb.Append("});");
        return sb.ToString();
    }
}
