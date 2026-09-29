namespace RequestExplorer.Models;

public enum AuthType
{
    None,
    Bearer,
    Basic,
    ApiKey
}

public enum ApiKeyLocation
{
    Header,
    Query
}
