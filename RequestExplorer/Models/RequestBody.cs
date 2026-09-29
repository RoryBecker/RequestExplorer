namespace RequestExplorer.Models;

public sealed class RequestBody
{
    /// <summary>Raw body text (JSON or plain text for Phase 1–5).</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Optional Content-Type override, e.g. application/json.</summary>
    public string? ContentType { get; set; } = "application/json";
}
