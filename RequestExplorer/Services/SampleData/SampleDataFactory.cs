using RequestExplorer.Models;
using RequestExplorer.Services.Tree;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.SampleData;

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
        sandbox.Requests.Add(new RequestDefinition
        {
            Name = "GET with query",
            Verb = HttpVerb.GET,
            Address = "https://httpbin.org/get?example=1&ready=true"
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

        // Address already contains a query string → Params table is filled on load (URL → Params).
        query.Requests.Add(new RequestDefinition
        {
            Name = "Query in URL",
            Verb = HttpVerb.GET,
            Address = "https://httpbin.org/get?search=widgets&limit=10&sort=name"
        });

        // Params table is the source → URL query is built on load (Params → URL).
        var viaParams = Req("Query via Params", HttpVerb.GET, "https://httpbin.org/get");
        viaParams.Params.Add(new KeyValueItem { Key = "page", Value = "1" });
        viaParams.Params.Add(new KeyValueItem { Key = "q", Value = "request-explorer" });
        viaParams.Params.Add(new KeyValueItem { Key = "include", Value = "meta" });
        query.Requests.Add(viaParams);

        // Encoded / multi-value style query for paste-into-URL demos.
        query.Requests.Add(new RequestDefinition
        {
            Name = "Encoded query",
            Verb = HttpVerb.GET,
            Address = "https://httpbin.org/get?q=hello%20world&tag=api&tag=demo"
        });

        var withHeaders = Req("Custom headers", HttpVerb.GET, "https://httpbin.org/headers");
        withHeaders.Headers.Add(new KeyValueItem { Key = "Accept", Value = "application/json" });
        withHeaders.Headers.Add(new KeyValueItem { Key = "X-Client", Value = "RequestExplorer" });
        query.Requests.Add(withHeaders);

        collection.Folders.Add(query);

        var xml = new FolderDefinition { Name = "XML & SOAP" };
        var getXml = Req("GET XML", HttpVerb.GET, "https://httpbin.org/xml");
        getXml.Headers.Add(new KeyValueItem { Key = "Accept", Value = "application/xml" });
        xml.Requests.Add(getXml);

        var getNote = Req("GET note.xml", HttpVerb.GET, "https://www.w3schools.com/xml/note.xml");
        getNote.Headers.Add(new KeyValueItem { Key = "Accept", Value = "application/xml, text/xml" });
        xml.Requests.Add(getNote);

        var soap = Req(
            "SOAP NumberToWords",
            HttpVerb.POST,
            "https://www.dataaccess.com/webservicesserver/NumberConversion.wso",
            body: """
                <?xml version="1.0" encoding="utf-8"?>
                <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
                  <soap:Body>
                    <NumberToWords xmlns="http://www.dataaccess.com/webservicesserver/">
                      <ubiNum>42</ubiNum>
                    </NumberToWords>
                  </soap:Body>
                </soap:Envelope>
                """,
            contentType: "text/xml; charset=utf-8");
        soap.Headers.Add(new KeyValueItem
        {
            Key = "SOAPAction",
            Value = "\"http://www.dataaccess.com/webservicesserver/NumberToWords\""
        });
        soap.Headers.Add(new KeyValueItem { Key = "Accept", Value = "text/xml" });
        xml.Requests.Add(soap);

        collection.Folders.Add(xml);

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
