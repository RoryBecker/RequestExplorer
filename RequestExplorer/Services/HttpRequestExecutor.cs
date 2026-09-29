using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RequestExplorer.Models;

namespace RequestExplorer.Services;

public sealed class HttpRequestExecutor : IDisposable
{
    public const long SoftBodyLimitBytes = 5L * 1024 * 1024;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);

    private readonly HttpClient _httpClient;

    public HttpRequestExecutor()
    {
        _httpClient = new HttpClient
        {
            Timeout = DefaultTimeout
        };
    }

    public async Task<HttpExecutionResult> SendAsync(RequestDefinition request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Address))
        {
            return Failure("Address is empty.");
        }

        HttpRequestMessage? message = null;
        var totalWatch = Stopwatch.StartNew();

        try
        {
            message = BuildRequestMessage(request);
            using var response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var timeToHeadersMs = totalWatch.Elapsed.TotalMilliseconds;

            var headers = new List<HeaderEntry>();
            foreach (var header in response.Headers)
            {
                headers.Add(new HeaderEntry(header.Key, string.Join(", ", header.Value)));
            }

            foreach (var header in response.Content.Headers)
            {
                headers.Add(new HeaderEntry(header.Key, string.Join(", ", header.Value)));
            }

            var cookies = new List<string>();
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                cookies.AddRange(setCookies);
            }

            var bodyWatch = Stopwatch.StartNew();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            bodyWatch.Stop();
            totalWatch.Stop();

            var bytes = memory.ToArray();
            var bodyText = TryPrettyPrintJson(DecodeBody(bytes, response.Content.Headers.ContentType));

            return new HttpExecutionResult
            {
                Succeeded = true,
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase,
                Body = bodyText,
                BodyByteCount = bytes.LongLength,
                ExceededSoftSizeLimit = bytes.LongLength > SoftBodyLimitBytes,
                Headers = headers,
                Cookies = cookies,
                Timing = new TimingInfo
                {
                    TotalMilliseconds = totalWatch.Elapsed.TotalMilliseconds,
                    TimeToHeadersMilliseconds = timeToHeadersMs,
                    BodyReadMilliseconds = bodyWatch.Elapsed.TotalMilliseconds
                }
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            totalWatch.Stop();
            return Failure("Request cancelled.", totalWatch.Elapsed.TotalMilliseconds);
        }
        catch (TaskCanceledException)
        {
            totalWatch.Stop();
            return Failure($"Request timed out after {DefaultTimeout.TotalSeconds:0}s.", totalWatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            totalWatch.Stop();
            return Failure($"Request timed out after {DefaultTimeout.TotalSeconds:0}s.", totalWatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            totalWatch.Stop();
            return Failure(ex.Message, totalWatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            message?.Dispose();
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private static HttpExecutionResult Failure(string message, double totalMs = 0) =>
        new()
        {
            Succeeded = false,
            ErrorMessage = message,
            Timing = new TimingInfo { TotalMilliseconds = totalMs }
        };

    private static HttpRequestMessage BuildRequestMessage(RequestDefinition request)
    {
        var uri = BuildUri(request);
        uri = ApplyApiKeyQuery(uri, request.Authentication);

        var method = new HttpMethod(request.Verb.ToString().ToUpperInvariant());
        var message = new HttpRequestMessage(method, uri);

        foreach (var header in request.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
        {
            if (IsContentHeader(header.Key))
            {
                continue;
            }

            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        ApplyHeaderAuthentication(message, request.Authentication);

        var mayHaveBody = request.Verb is HttpVerb.POST or HttpVerb.PUT or HttpVerb.PATCH
                          || !string.IsNullOrEmpty(request.Body.Content);

        if (mayHaveBody && !string.IsNullOrEmpty(request.Body.Content))
        {
            var contentType = ResolveContentType(request);
            message.Content = new StringContent(request.Body.Content, Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            }
        }

        return message;
    }

    private static void ApplyHeaderAuthentication(HttpRequestMessage message, AuthenticationSettings auth)
    {
        switch (auth.Type)
        {
            case AuthType.Bearer when !string.IsNullOrWhiteSpace(auth.BearerToken):
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.BearerToken);
                break;

            case AuthType.Basic:
            {
                var raw = $"{auth.BasicUsername}:{auth.BasicPassword}";
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
                break;
            }

            case AuthType.ApiKey when auth.ApiKeyLocation == ApiKeyLocation.Header
                                      && !string.IsNullOrWhiteSpace(auth.ApiKeyName):
                message.Headers.TryAddWithoutValidation(auth.ApiKeyName, auth.ApiKeyValue);
                break;
        }
    }

    private static Uri ApplyApiKeyQuery(Uri uri, AuthenticationSettings auth)
    {
        if (auth.Type != AuthType.ApiKey
            || auth.ApiKeyLocation != ApiKeyLocation.Query
            || string.IsNullOrWhiteSpace(auth.ApiKeyName))
        {
            return uri;
        }

        return AppendQuery(uri, auth.ApiKeyName, auth.ApiKeyValue);
    }

    private static Uri BuildUri(RequestDefinition request)
    {
        var address = request.Address.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException("Address is empty.");
        }

        // Strip any query already present on the address; enabled Params are the source of truth.
        var baseAddress = StripQueryForSend(address);
        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Invalid address: '{request.Address}'");
        }

        var uri = baseUri;
        foreach (var param in request.Params.Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key)))
        {
            uri = AppendQuery(uri, param.Key, param.Value);
        }

        return uri;
    }

    private static string StripQueryForSend(string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var absolute))
        {
            var builder = new UriBuilder(absolute) { Query = string.Empty };
            var built = builder.Uri.GetLeftPart(UriPartial.Path);
            if (!string.IsNullOrEmpty(absolute.Fragment))
            {
                built += absolute.Fragment;
            }

            return built;
        }

        var queryIndex = address.IndexOf('?');
        if (queryIndex < 0)
        {
            return address;
        }

        var fragmentIndex = address.IndexOf('#', queryIndex);
        return fragmentIndex >= 0
            ? address[..queryIndex] + address[fragmentIndex..]
            : address[..queryIndex];
    }

    private static Uri AppendQuery(Uri uri, string key, string? value)
    {
        var builder = new UriBuilder(uri);
        var existing = builder.Query.TrimStart('?');
        var pair = $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value ?? string.Empty)}";
        builder.Query = string.IsNullOrEmpty(existing) ? pair : $"{existing}&{pair}";
        return builder.Uri;
    }

    private static string? ResolveContentType(RequestDefinition request)
    {
        if (!string.IsNullOrWhiteSpace(request.Body.ContentType))
        {
            return request.Body.ContentType;
        }

        return request.Headers
            .FirstOrDefault(h => h.IsEnabled && h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static bool IsContentHeader(string name) =>
        name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);

    private static string DecodeBody(byte[] bytes, MediaTypeHeaderValue? contentType)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var encoding = Encoding.UTF8;
        if (contentType?.CharSet is { Length: > 0 } charset)
        {
            try
            {
                encoding = Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
            }
        }

        return encoding.GetString(bytes);
    }

    private static string TryPrettyPrintJson(string body)
    {
        var trimmed = body.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is not ('{' or '['))
        {
            return body;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
