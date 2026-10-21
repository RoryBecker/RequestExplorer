using System.Text;
using RequestExplorer.Models;

namespace RequestExplorer.Services.Export.FormatProviders;

public sealed class PythonRequestsExportFormatProvider : IExportFormatProvider
{
    public RequestExportFormat Format => RequestExportFormat.PythonRequests;

    public string DisplayName => "Python requests";

    public string Export(RequestDefinition request)
    {
        var url = RequestExportSupport.BuildUrl(request);
        var method = request.Verb.ToString().ToLowerInvariant();
        var sb = new StringBuilder();
        sb.Append("import requests\n\n");
        sb.Append("url = ").Append(RequestExportSupport.PyString(url)).Append('\n');
        sb.Append("headers = {\n");
        foreach (var (name, value) in RequestExportSupport.GetEffectiveHeaders(request))
        {
            sb.Append("    ").Append(RequestExportSupport.PyString(name)).Append(": ").Append(RequestExportSupport.PyString(value)).Append(",\n");
        }

        sb.Append("}\n");
        if (RequestExportSupport.HasBody(request))
        {
            sb.Append("data = ").Append(RequestExportSupport.PyString(request.Body.Content)).Append('\n');
            sb.Append("response = requests.").Append(method)
              .Append("(url, headers=headers, data=data)\n");
        }
        else
        {
            sb.Append("response = requests.").Append(method)
              .Append("(url, headers=headers)\n");
        }

        sb.Append("print(response.status_code)\n");
        sb.Append("print(response.text)");
        return sb.ToString();
    }
}
