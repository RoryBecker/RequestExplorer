namespace RequestExplorer.Models;

public sealed class AuthenticationSettings
{
    public AuthType Type { get; set; } = AuthType.None;

    public string BearerToken { get; set; } = string.Empty;

    public string BasicUsername { get; set; } = string.Empty;
    public string BasicPassword { get; set; } = string.Empty;

    public string ApiKeyName { get; set; } = "X-Api-Key";
    public string ApiKeyValue { get; set; } = string.Empty;
    public ApiKeyLocation ApiKeyLocation { get; set; } = ApiKeyLocation.Header;
}
