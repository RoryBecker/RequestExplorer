using RequestExplorer.Models;
using RequestExplorer.ViewModels;

namespace RequestExplorer.Services;

public static class SampleDataFactory
{
    public static IReadOnlyList<CollectionDefinition> CreateDemoDefinitions()
    {
        var sandbox = new CollectionDefinition { Name = "Sandbox" };
        sandbox.Requests.Add(new RequestDefinition
        {
            Name = "Blank GET",
            Verb = HttpVerb.GET,
            Address = "https://httpbin.org/get"
        });

        return [BuildHttpbinCollection(), sandbox];
    }

    public static IReadOnlyList<CollectionNodeViewModel> CreateDemoWorkspace() =>
        CreateDemoDefinitions().Select(CollectionTreeMapper.ToViewModel).ToList();

    private static CollectionDefinition BuildHttpbinCollection()
    {
        var collection = new CollectionDefinition { Name = "httpbin.org Demo" };

        var methods = new FolderDefinition { Name = "HTTP Methods" };
        methods.Requests.Add(Req("GET", HttpVerb.GET, "https://httpbin.org/get"));
        methods.Requests.Add(Req("POST JSON", HttpVerb.POST, "https://httpbin.org/post",
            body: """{"message":"hello"}""", contentType: "application/json"));
        methods.Requests.Add(Req("PUT", HttpVerb.PUT, "https://httpbin.org/put",
            body: """{"update":true}""", contentType: "application/json"));
        methods.Requests.Add(Req("PATCH", HttpVerb.PATCH, "https://httpbin.org/patch",
            body: """{"patch":1}""", contentType: "application/json"));
        methods.Requests.Add(Req("DELETE", HttpVerb.DELETE, "https://httpbin.org/delete"));
        collection.Folders.Add(methods);

        var auth = new FolderDefinition { Name = "Authentication" };
        auth.Requests.Add(new RequestDefinition
        {
            Name = "Bearer token",
            Verb = HttpVerb.GET,
            Address = "https://httpbin.org/bearer",
            Authentication = new AuthenticationSettings
            {
                Type = AuthType.Bearer,
                BearerToken = "demo-token"
            }
        });
        auth.Requests.Add(new RequestDefinition
        {
            Name = "Basic auth",
            Verb = HttpVerb.GET,
            Address = "https://httpbin.org/basic-auth/user/passwd",
            Authentication = new AuthenticationSettings
            {
                Type = AuthType.Basic,
                BasicUsername = "user",
                BasicPassword = "passwd"
            }
        });
        auth.Requests.Add(new RequestDefinition
        {
            Name = "API key header",
            Verb = HttpVerb.GET,
            Address = "https://httpbin.org/headers",
            Authentication = new AuthenticationSettings
            {
                Type = AuthType.ApiKey,
                ApiKeyName = "X-Api-Key",
                ApiKeyValue = "demo-key",
                ApiKeyLocation = ApiKeyLocation.Header
            }
        });
        collection.Folders.Add(auth);

        var status = new FolderDefinition { Name = "Status Codes" };
        status.Requests.Add(Req("200 OK", HttpVerb.GET, "https://httpbin.org/status/200"));
        status.Requests.Add(Req("404 Not Found", HttpVerb.GET, "https://httpbin.org/status/404"));
        status.Requests.Add(Req("500 Server Error", HttpVerb.GET, "https://httpbin.org/status/500"));
        collection.Folders.Add(status);

        var query = new FolderDefinition { Name = "Query & Headers" };
        var withParams = Req("Query params", HttpVerb.GET, "https://httpbin.org/get");
        withParams.Params.Add(new KeyValueItem { Key = "page", Value = "1" });
        withParams.Params.Add(new KeyValueItem { Key = "q", Value = "request-explorer" });
        withParams.Headers.Add(new KeyValueItem { Key = "Accept", Value = "application/json" });
        query.Requests.Add(withParams);
        collection.Folders.Add(query);

        return collection;
    }

    private static RequestDefinition Req(
        string name,
        HttpVerb verb,
        string address,
        string? body = null,
        string? contentType = null)
    {
        var request = new RequestDefinition
        {
            Name = name,
            Verb = verb,
            Address = address
        };

        if (body is not null)
        {
            request.Body.Content = body;
            request.Body.ContentType = contentType ?? "application/json";
        }

        return request;
    }
}
