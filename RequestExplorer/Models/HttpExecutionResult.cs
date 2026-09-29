namespace RequestExplorer.Models;

public sealed class HttpExecutionResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public int? StatusCode { get; init; }
    public string? ReasonPhrase { get; init; }
    public string Body { get; init; } = string.Empty;
    public long BodyByteCount { get; init; }
    public bool ExceededSoftSizeLimit { get; init; }
    public IReadOnlyList<HeaderEntry> Headers { get; init; } = [];
    public IReadOnlyList<string> Cookies { get; init; } = [];
    public TimingInfo Timing { get; init; } = new();
}

public sealed class HeaderEntry
{
    public HeaderEntry(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public string Value { get; }
}

public sealed class TimingInfo
{
    public double TotalMilliseconds { get; init; }
    public double TimeToHeadersMilliseconds { get; init; }
    public double BodyReadMilliseconds { get; init; }
}
