namespace RequestExplorer.Services.Undo;

public enum RequestEditorField
{
    Verb,
    AuthType,
    BearerToken,
    BasicUsername,
    BasicPassword,
    ApiKeyName,
    ApiKeyValue,
    ApiKeyLocation,
    ExpectedStatusCode,
    ExpectBodyEnabled,
    ExpectBodyContains,
    ExpectedBody,
    ExpectTimingEnabled,
    ExpectedTimingMilliseconds
}
