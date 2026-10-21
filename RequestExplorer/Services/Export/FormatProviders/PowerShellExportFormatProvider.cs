using System.Text;
using RequestExplorer.Models;

namespace RequestExplorer.Services.Export.FormatProviders;

public sealed class PowerShellExportFormatProvider : IExportFormatProvider
{
    public RequestExportFormat Format => RequestExportFormat.PowerShell;

    public string DisplayName => "PowerShell";

    public string Export(RequestDefinition request)
    {
        var url = RequestExportSupport.BuildUrl(request);
        var method = request.Verb.ToString().ToUpperInvariant();
        var sb = new StringBuilder();
        sb.Append("$headers = @{").Append('\n');
        foreach (var (name, value) in RequestExportSupport.GetEffectiveHeaders(request))
        {
            sb.Append("  ").Append(RequestExportSupport.PsSingleQuote(name)).Append(" = ").Append(RequestExportSupport.PsSingleQuote(value)).Append('\n');
        }

        sb.Append('}').Append('\n');
        sb.Append("Invoke-RestMethod");
        sb.Append(" -Method ").Append(method);
        sb.Append(" -Uri ").Append(RequestExportSupport.PsSingleQuote(url));
        sb.Append(" -Headers $headers");
        if (RequestExportSupport.HasBody(request))
        {
            sb.Append(" -Body ").Append(RequestExportSupport.PsSingleQuote(request.Body.Content));
        }

        return sb.ToString();
    }
}
