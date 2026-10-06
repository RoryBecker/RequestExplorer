namespace RequestExplorer.Models;

/// <summary>A WebRequest: verb, address, params, headers, auth, and optional body.</summary>
public sealed class RequestDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Request";
    public HttpVerb Verb { get; set; } = HttpVerb.GET;
    public string Address { get; set; } = string.Empty;
    public List<KeyValueItem> Params { get; set; } = [];
    public List<KeyValueItem> Headers { get; set; } = [];
    public AuthenticationSettings Authentication { get; set; } = new();
    public RequestBody Body { get; set; } = new();

    /// <summary>HTTP status code expected on success (used by Run All expectations).</summary>
    public int ExpectedStatusCode { get; set; } = 200;
}
