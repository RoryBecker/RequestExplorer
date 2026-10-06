using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RequestExplorer.Models;

namespace RequestExplorer.Services;

public sealed class HttpRequestExecutor : IDisposable
{
    public const long SoftBodyLimitBytes = 5L * 1024 * 1024;
    public const int DefaultTimeoutSeconds = 30;

    private readonly HttpClient _httpClient;
    private TimeSpan _timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);
    private string _timeoutLabel = $"{DefaultTimeoutSeconds}s";

    public HttpRequestExecutor()
    {
        // Timeout is enforced per-send via CancellationToken — HttpClient.Timeout cannot be
        // changed after the first request, which would crash Settings after a send.
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    /// <summary>Applies global timeout settings. Dev mode uses an infinite timeout.</summary>
    public void ApplyTimeoutSettings(int timeoutSeconds, bool devModeIgnoreTimeout)
    {
        if (devModeIgnoreTimeout)
        {
            _timeout = Timeout.InfiniteTimeSpan;
            _timeoutLabel = "∞ (dev mode)";
        }
        else
        {
            var seconds = Math.Clamp(timeoutSeconds, 1, 3600);
            _timeout = TimeSpan.FromSeconds(seconds);
            _timeoutLabel = $"{seconds}s";
        }
    }

    public async Task<HttpExecutionResult> SendAsync(RequestDefinition request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Address))
        {
            return Failure("Address is empty.");
        }

        var timeout = _timeout;
        var timeoutLabel = _timeoutLabel;
        HttpRequestMessage? message = null;
        var totalWatch = Stopwatch.StartNew();
        var rawRequest = string.Empty;

        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        try
        {
            message = BuildRequestMessage(request);
            rawRequest = FormatRawRequest(message, request.Body.Content);

            var sendToken = cancellationToken;
            if (timeout != Timeout.InfiniteTimeSpan)
            {
                timeoutCts = new CancellationTokenSource(timeout);
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                sendToken = linkedCts.Token;
            }

            using var response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, sendToken)
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

            var contentType = response.Content.Headers.ContentType?.ToString();
            var contentDisposition = response.Content.Headers.ContentDisposition?.ToString()
                ?? headers.FirstOrDefault(h => h.Name.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase))?.Value;

            var bodyWatch = Stopwatch.StartNew();
            await using var stream = await response.Content.ReadAsStreamAsync(sendToken)
                .ConfigureAwait(false);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, sendToken).ConfigureAwait(false);
            bodyWatch.Stop();
            totalWatch.Stop();

            var bytes = memory.ToArray();
            var isBinary = IsLikelyBinary(contentType, bytes);
            var isDownloadable = IsDownloadable(contentDisposition, contentType, isBinary);
            var suggestedName = SuggestFileName(contentDisposition, message.RequestUri, contentType, isBinary);
            var bodyText = isBinary
                ? $"[Binary content: {FormatBytes(bytes.LongLength)}"
                  + (string.IsNullOrWhiteSpace(contentType) ? "]" : $", {contentType}]")
                : TryPrettyPrintJson(DecodeBody(bytes, response.Content.Headers.ContentType));

            var rawResponse = FormatRawResponse(
                (int)response.StatusCode,
                response.ReasonPhrase,
                headers,
                isBinary ? $"[Binary content: {bytes.LongLength} bytes]" : bodyText);

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
                BodyBytes = bytes,
                ContentType = contentType,
                SuggestedFileName = suggestedName,
                IsDownloadable = isDownloadable,
                RawRequest = rawRequest,
                RawResponse = rawResponse,
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
            return Failure("Request cancelled.", totalWatch.Elapsed.TotalMilliseconds, rawRequest);
        }
        catch (OperationCanceledException) when (timeoutCts is { IsCancellationRequested: true })
        {
            totalWatch.Stop();
            return Failure($"Request timed out after {timeoutLabel}.", totalWatch.Elapsed.TotalMilliseconds, rawRequest);
        }
        catch (TaskCanceledException)
        {
            totalWatch.Stop();
            if (cancellationToken.IsCancellationRequested)
            {
                return Failure("Request cancelled.", totalWatch.Elapsed.TotalMilliseconds, rawRequest);
            }

            return Failure($"Request timed out after {timeoutLabel}.", totalWatch.Elapsed.TotalMilliseconds, rawRequest);
        }
        catch (Exception ex)
        {
            totalWatch.Stop();
            return Failure(ex.Message, totalWatch.Elapsed.TotalMilliseconds, rawRequest);
        }
        finally
        {
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
            message?.Dispose();
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private static HttpExecutionResult Failure(string message, double totalMs = 0, string rawRequest = "") =>
        new()
        {
            Succeeded = false,
            ErrorMessage = message,
            RawRequest = rawRequest,
            RawResponse = message,
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

    private static string FormatRawRequest(HttpRequestMessage message, string? body)
    {
        var sb = new StringBuilder();
        sb.Append(message.Method.Method)
            .Append(' ')
            .Append(message.RequestUri)
            .Append(" HTTP/1.1")
            .Append('\n');

        foreach (var header in message.Headers)
        {
            foreach (var value in header.Value)
            {
                sb.Append(header.Key).Append(": ").Append(value).Append('\n');
            }
        }

        if (message.Content is not null)
        {
            foreach (var header in message.Content.Headers)
            {
                foreach (var value in header.Value)
                {
                    sb.Append(header.Key).Append(": ").Append(value).Append('\n');
                }
            }
        }

        if (!string.IsNullOrEmpty(body))
        {
            sb.Append('\n').Append(body);
        }

        return sb.ToString();
    }

    private static string FormatRawResponse(
        int statusCode,
        string? reasonPhrase,
        IReadOnlyList<HeaderEntry> headers,
        string body)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ")
            .Append(statusCode)
            .Append(' ')
            .Append(reasonPhrase ?? string.Empty)
            .Append('\n');

        foreach (var header in headers)
        {
            sb.Append(header.Name).Append(": ").Append(header.Value).Append('\n');
        }

        sb.Append('\n').Append(body);
        return sb.ToString();
    }

    private static bool IsDownloadable(string? contentDisposition, string? contentType, bool isBinary)
    {
        if (!string.IsNullOrWhiteSpace(contentDisposition)
            && contentDisposition.Contains("attachment", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return isBinary;
    }

    private static bool IsLikelyBinary(string? contentType, byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var media = contentType.Split(';')[0].Trim();
            if (media.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || media.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || media.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
                || media.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
                || media.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
                || media.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
                || media.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (media.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || media.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                || media.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                || media.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
                || media.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
                || media.Equals("application/zip", StringComparison.OrdinalIgnoreCase)
                || media.Contains("msword", StringComparison.OrdinalIgnoreCase)
                || media.Contains("officedocument", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // NUL bytes in the first chunk ⇒ treat as binary.
        var sample = Math.Min(bytes.Length, 512);
        for (var i = 0; i < sample; i++)
        {
            if (bytes[i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string SuggestFileName(
        string? contentDisposition,
        Uri? requestUri,
        string? contentType,
        bool isBinary)
    {
        var fromDisposition = TryParseContentDispositionFileName(contentDisposition);
        if (!string.IsNullOrWhiteSpace(fromDisposition))
        {
            return fromDisposition;
        }

        if (requestUri is not null)
        {
            var segment = requestUri.Segments.LastOrDefault()?.Trim('/');
            if (!string.IsNullOrWhiteSpace(segment) && segment.Contains('.', StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(segment);
            }
        }

        var ext = GuessExtension(contentType, isBinary);
        return "response" + ext;
    }

    private static string? TryParseContentDispositionFileName(string? contentDisposition)
    {
        if (string.IsNullOrWhiteSpace(contentDisposition))
        {
            return null;
        }

        var match = Regex.Match(
            contentDisposition,
            @"filename\*=UTF-8''(?<name>[^;]+)|filename=""(?<name>[^""]+)|filename=(?<name>[^;]+)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(name) ? null : Uri.UnescapeDataString(name);
    }

    private static string GuessExtension(string? contentType, bool isBinary)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return isBinary ? ".bin" : ".txt";
        }

        var media = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return media switch
        {
            "application/json" => ".json",
            "application/xml" or "text/xml" => ".xml",
            "text/html" => ".html",
            "text/csv" => ".csv",
            "application/pdf" => ".pdf",
            "application/zip" => ".zip",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ when media.StartsWith("text/", StringComparison.Ordinal) => ".txt",
            _ => ".bin"
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:0.0} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):0.00} MB";
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
