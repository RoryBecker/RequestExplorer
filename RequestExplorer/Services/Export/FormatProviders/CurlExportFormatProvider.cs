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

        if (RequestExportSupport.HasFormDataBody(request))
        {
            foreach (var field in request.Body.FormFields
                         ?? Enumerable.Empty<FormFieldItem>())
            {
                if (!field.IsEnabled || string.IsNullOrWhiteSpace(field.Key))
                {
                    continue;
                }

                if (field.Type == FormFieldType.File)
                {
                    if (string.IsNullOrWhiteSpace(field.Base64Content)
                        && string.IsNullOrWhiteSpace(field.FileName))
                    {
                        continue;
                    }

                    var fileName = string.IsNullOrWhiteSpace(field.FileName) ? "file" : field.FileName;
                    // Bytes live as base64 in the collection; curl needs a path — use the file name.
                    sb.Append(sep)
                        .Append("--form ")
                        .Append(RequestExportSupport.ShellSingleQuote($"{field.Key}=@{fileName}"));
                }
                else
                {
                    sb.Append(sep)
                        .Append("--form ")
                        .Append(RequestExportSupport.ShellSingleQuote($"{field.Key}={field.Value}"));
                }
            }
        }
        else if (RequestExportSupport.HasRawBody(request))
        {
            sb.Append(sep).Append("--data-raw ").Append(RequestExportSupport.ShellSingleQuote(request.Body.Content));
        }

        return sb.ToString();
    }
}
