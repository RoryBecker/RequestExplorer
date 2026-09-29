using System.Text;
using System.Text.Json;
using RequestExplorer.Models;
using RequestExplorer.Services.Postman;

namespace RequestExplorer.Services.Export;

public static class RequestExportService
{
    public static IReadOnlyList<RequestExportFormatOption> Formats { get; } =
    [
        new(RequestExportFormat.Curl, "cURL"),
        new(RequestExportFormat.Http, "HTTP"),
        new(RequestExportFormat.PostmanCollection, "Postman Collection v2.1"),
        new(RequestExportFormat.Fetch, "JavaScript Fetch"),
        new(RequestExportFormat.PowerShell, "PowerShell"),
        new(RequestExportFormat.PythonRequests, "Python requests"),
        new(RequestExportFormat.Httpie, "HTTPie")
    ];

    public static string Export(RequestDefinition request, RequestExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Address))
        {
            return "// No URL on this request yet.";
        }

        return format switch
        {
            RequestExportFormat.Curl => ToCurl(request),
            RequestExportFormat.Http => ToHttp(request),
            RequestExportFormat.PostmanCollection => ToPostman(request),
            RequestExportFormat.Fetch => ToFetch(request),
            RequestExportFormat.PowerShell => ToPowerShell(request),
            RequestExportFormat.PythonRequests => ToPython(request),
            RequestExportFormat.Httpie => ToHttpie(request),
            _ => ToCurl(request)
        };
    }

    private static string ToCurl(RequestDefinition request)
    {
        var sb = new StringBuilder();
        var url = BuildUrl(request);
        var method = request.Verb.ToString().ToUpperInvariant();

        sb.Append("curl --request ").Append(method);
        sb.Append(" \\\n  --url ").Append(ShellSingleQuote(url));

        foreach (var (name, value) in GetEffectiveHeaders(request))
        {
            sb.Append(" \\\n  --header ").Append(ShellSingleQuote($"{name}: {value}"));
        }

        if (HasBody(request))
        {
            sb.Append(" \\\n  --data-raw ").Append(ShellSingleQuote(request.Body.Content));
        }

        return sb.ToString();
    }

    private static string ToHttp(RequestDefinition request)
    {
        var url = new Uri(BuildUrl(request));
        var pathAndQuery = string.IsNullOrEmpty(url.PathAndQuery) ? "/" : url.PathAndQuery;
        var sb = new StringBuilder();
        sb.Append(request.Verb.ToString().ToUpperInvariant())
          .Append(' ')
          .Append(pathAndQuery)
          .Append(" HTTP/1.1\n");
        sb.Append("Host: ").Append(url.IsDefaultPort ? url.Host : $"{url.Host}:{url.Port}").Append('\n');

        foreach (var (name, value) in GetEffectiveHeaders(request))
        {
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.Append(name).Append(": ").Append(value).Append('\n');
        }

        if (HasBody(request))
        {
            sb.Append('\n').Append(request.Body.Content);
        }

        return sb.ToString();
    }

    private static string ToPostman(RequestDefinition request)
    {
        var document = PostmanCollectionMapper.ToCollectionDocument(request);
        return JsonSerializer.Serialize(document, PostmanCollectionMapper.JsonOptions);
    }

    private static string ToFetch(RequestDefinition request)
    {
        var url = BuildUrl(request);
        var method = request.Verb.ToString().ToUpperInvariant();
        var sb = new StringBuilder();
        sb.Append("fetch(").Append(JsString(url)).Append(", {\n");
        sb.Append("  method: ").Append(JsString(method)).Append(",\n");

        var headers = GetEffectiveHeaders(request).ToList();
        if (headers.Count > 0)
        {
            sb.Append("  headers: {\n");
            for (var i = 0; i < headers.Count; i++)
            {
                var (name, value) = headers[i];
                sb.Append("    ").Append(JsString(name)).Append(": ").Append(JsString(value));
                if (i < headers.Count - 1)
                {
                    sb.Append(',');
                }

                sb.Append('\n');
            }

            sb.Append("  }");
            if (HasBody(request))
            {
                sb.Append(',');
            }

            sb.Append('\n');
        }

        if (HasBody(request))
        {
            sb.Append("  body: ").Append(JsString(request.Body.Content)).Append('\n');
        }

        sb.Append("});");
        return sb.ToString();
    }

    private static string ToPowerShell(RequestDefinition request)
    {
        var url = BuildUrl(request);
        var method = request.Verb.ToString().ToUpperInvariant();
        var sb = new StringBuilder();
        sb.Append("$headers = @{").Append('\n');
        foreach (var (name, value) in GetEffectiveHeaders(request))
        {
            sb.Append("  ").Append(PsSingleQuote(name)).Append(" = ").Append(PsSingleQuote(value)).Append('\n');
        }

        sb.Append('}').Append('\n');
        sb.Append("Invoke-RestMethod");
        sb.Append(" -Method ").Append(method);
        sb.Append(" -Uri ").Append(PsSingleQuote(url));
        sb.Append(" -Headers $headers");
        if (HasBody(request))
        {
            sb.Append(" -Body ").Append(PsSingleQuote(request.Body.Content));
        }

        return sb.ToString();
    }

    private static string ToPython(RequestDefinition request)
    {
        var url = BuildUrl(request);
        var method = request.Verb.ToString().ToLowerInvariant();
        var sb = new StringBuilder();
        sb.Append("import requests\n\n");
        sb.Append("url = ").Append(PyString(url)).Append('\n');
        sb.Append("headers = {\n");
        foreach (var (name, value) in GetEffectiveHeaders(request))
        {
            sb.Append("    ").Append(PyString(name)).Append(": ").Append(PyString(value)).Append(",\n");
        }

        sb.Append("}\n");
        if (HasBody(request))
        {
            sb.Append("data = ").Append(PyString(request.Body.Content)).Append('\n');
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

    private static string ToHttpie(RequestDefinition request)
    {
        var url = BuildUrl(request);
        var method = request.Verb.ToString().ToUpperInvariant();
        var sb = new StringBuilder();
        sb.Append("http ").Append(method).Append(' ').Append(ShellEscapeHttpieUrl(url));

        foreach (var (name, value) in GetEffectiveHeaders(request))
        {
            sb.Append(" \\\n  ").Append(name).Append(':').Append(ShellSingleQuote(value));
        }

        if (HasBody(request))
        {
            // Prefer raw body via stdin-style flag for arbitrary content.
            sb.Append(" \\\n  --raw ").Append(ShellSingleQuote(request.Body.Content));
        }

        return sb.ToString();
    }

    private static bool HasBody(RequestDefinition request) =>
        !string.IsNullOrEmpty(request.Body.Content)
        && (request.Verb is HttpVerb.POST or HttpVerb.PUT or HttpVerb.PATCH
            || !string.IsNullOrEmpty(request.Body.Content));

    private static IEnumerable<(string Name, string Value)> GetEffectiveHeaders(RequestDefinition request)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in request.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
        {
            if (seen.Add(header.Key))
            {
                yield return (header.Key, header.Value ?? string.Empty);
            }
        }

        var auth = request.Authentication;
        switch (auth.Type)
        {
            case AuthType.Bearer when !string.IsNullOrWhiteSpace(auth.BearerToken):
                if (seen.Add("Authorization"))
                {
                    yield return ("Authorization", $"Bearer {auth.BearerToken}");
                }

                break;
            case AuthType.Basic:
            {
                if (seen.Add("Authorization"))
                {
                    var raw = $"{auth.BasicUsername}:{auth.BasicPassword}";
                    var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                    yield return ("Authorization", $"Basic {token}");
                }

                break;
            }
            case AuthType.ApiKey when auth.ApiKeyLocation == ApiKeyLocation.Header
                                      && !string.IsNullOrWhiteSpace(auth.ApiKeyName):
                if (seen.Add(auth.ApiKeyName))
                {
                    yield return (auth.ApiKeyName, auth.ApiKeyValue ?? string.Empty);
                }

                break;
        }

        if (HasBody(request))
        {
            var contentType = string.IsNullOrWhiteSpace(request.Body.ContentType)
                ? "application/json"
                : request.Body.ContentType!;
            if (seen.Add("Content-Type"))
            {
                yield return ("Content-Type", contentType);
            }
        }
    }

    private static string BuildUrl(RequestDefinition request)
    {
        var address = request.Address.Trim();
        var baseAddress = StripQuery(address);
        var pairs = new List<string>();
        foreach (var param in request.Params.Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key)))
        {
            pairs.Add(
                $"{VariableResolver.EscapeDataStringPreservingTokens(param.Key)}={VariableResolver.EscapeDataStringPreservingTokens(param.Value)}");
        }

        var auth = request.Authentication;
        if (auth.Type == AuthType.ApiKey
            && auth.ApiKeyLocation == ApiKeyLocation.Query
            && !string.IsNullOrWhiteSpace(auth.ApiKeyName))
        {
            pairs.Add(
                $"{VariableResolver.EscapeDataStringPreservingTokens(auth.ApiKeyName)}={VariableResolver.EscapeDataStringPreservingTokens(auth.ApiKeyValue)}");
        }

        return pairs.Count == 0
            ? baseAddress
            : $"{baseAddress}?{string.Join("&", pairs)}";
    }

    private static string BuildQuery(RequestDefinition request)
    {
        var pairs = request.Params
            .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key))
            .Select(p =>
                $"{VariableResolver.EscapeDataStringPreservingTokens(p.Key)}={VariableResolver.EscapeDataStringPreservingTokens(p.Value)}");
        return string.Join("&", pairs);
    }

    private static string StripQuery(string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var absolute))
        {
            var builder = new UriBuilder(absolute) { Query = string.Empty };
            return builder.Uri.GetLeftPart(UriPartial.Path);
        }

        var q = address.IndexOf('?');
        return q < 0 ? address : address[..q];
    }

    private static string ShellSingleQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string ShellEscapeHttpieUrl(string url) =>
        url.Contains(' ', StringComparison.Ordinal) ? ShellSingleQuote(url) : url;

    private static string JsString(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) + "\"";

    private static string PyString(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    private static string PsSingleQuote(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
