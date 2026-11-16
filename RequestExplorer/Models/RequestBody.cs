namespace RequestExplorer.Models;

public sealed class RequestBody
{
    /// <summary>Raw vs multipart form-data. Default Raw keeps existing collections unchanged.</summary>
    public RequestBodyMode Mode { get; set; } = RequestBodyMode.Raw;

    /// <summary>Raw body text (JSON or plain text).</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Optional Content-Type override for raw bodies, e.g. application/json.</summary>
    public string? ContentType { get; set; } = "application/json";

    /// <summary>Multipart form fields when <see cref="Mode"/> is <see cref="RequestBodyMode.FormData"/>.</summary>
    public List<FormFieldItem> FormFields { get; set; } = [];
}
