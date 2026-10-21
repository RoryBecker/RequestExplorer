using System.Text;
using RequestExplorer.Models;
using RequestExplorer.Services.Variables;

namespace RequestExplorer.Services.Export;

internal static class RequestExportSupport
{
    public static bool HasBody(RequestDefinition request) =>
        !string.IsNullOrEmpty(request.Body.Content)
        && (request.Verb is HttpVerb.POST or HttpVerb.PUT or HttpVerb.PATCH
            || !string.IsNullOrEmpty(request.Body.Content));

    public static IEnumerable<(string Name, string Value)> GetEffectiveHeaders(RequestDefinition request)
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

    public static string BuildUrl(RequestDefinition request)
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

    public static string ShellSingleQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    public static string ShellEscapeHttpieUrl(string url) =>
        url.Contains(' ', StringComparison.Ordinal) ? ShellSingleQuote(url) : url;

    public static string JsString(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) + "\"";

    public static string PyString(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    public static string PsSingleQuote(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

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
}
