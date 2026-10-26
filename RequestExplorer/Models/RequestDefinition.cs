using System.Text.Json.Serialization;

namespace RequestExplorer.Models;

/// <summary>A WebRequest: verb, address, params, headers, auth, and optional body.</summary>
public sealed class RequestDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Request";

    /// <summary>Optional notes shown in the request editor.</summary>
    public string? Description { get; set; }

    /// <summary>When true, this request is skipped by Run All (still diagnosed).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OptOutOfAutoRun { get; set; }
    public HttpVerb Verb { get; set; } = HttpVerb.GET;
    public string Address { get; set; } = string.Empty;
    public List<KeyValueItem> Params { get; set; } = [];
    public List<KeyValueItem> Headers { get; set; } = [];
    public AuthenticationSettings Authentication { get; set; } = new();
    public RequestBody Body { get; set; } = new();

    /// <summary>HTTP status code expected on success (used by Run All expectations).</summary>
    public int ExpectedStatusCode { get; set; } = 200;

    /// <summary>When true, response body is checked against <see cref="ExpectedBody"/>.</summary>
    public bool ExpectBodyEnabled { get; set; }

    /// <summary>
    /// When true with <see cref="ExpectBodyEnabled"/>, the body must contain
    /// <see cref="ExpectedBody"/>; otherwise it must match exactly.
    /// </summary>
    public bool ExpectBodyContains { get; set; }

    public string ExpectedBody { get; set; } = string.Empty;

    /// <summary>When true, total response time must be ≤ <see cref="ExpectedTimingMilliseconds"/>.</summary>
    public bool ExpectTimingEnabled { get; set; } = true;

    /// <summary>Maximum allowed total response time in milliseconds.</summary>
    public double ExpectedTimingMilliseconds { get; set; } = 300;
}
