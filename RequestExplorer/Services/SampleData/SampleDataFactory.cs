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

        return [BuildFeatureTourCollection(), BuildHttpbinCollection(), sandbox];
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

    /// <summary>
    /// Walkthrough collection: send, variables, tree moves, and Postman export vs flatten.
    /// </summary>
    private static CollectionDefinition BuildFeatureTourCollection()
    {
        var collection = new CollectionDefinition { Name = "Feature Tour" };
        collection.Variables.Add(Var("host", "https://httpbin.org"));
        collection.Variables.Add(Var("client", "RequestExplorer"));
        collection.Variables.Add(Var("region", "global"));

        Add(collection, BuildSendFolder());
        Add(collection, BuildVariablesFolder());
        Add(collection, BuildMovementFolder());
        Add(collection, BuildExportingFolder());
        return collection;
    }

    private static FolderDefinition BuildSendFolder()
    {
        var folder = new FolderDefinition { Name = "Send a request" };
        Add(folder, TourReq("GET", HttpVerb.GET, "{{host}}/get"));
        Add(folder, TourReq("POST JSON", HttpVerb.POST, "{{host}}/post",
            body: """{"message":"hello from Feature Tour"}""",
            contentType: "application/json"));
        Add(folder, TourReq("PUT", HttpVerb.PUT, "{{host}}/put",
            body: """{"update":true}""",
            contentType: "application/json"));
        Add(folder, TourReq("PATCH", HttpVerb.PATCH, "{{host}}/patch",
            body: """{"patch":1}""",
            contentType: "application/json"));
        Add(folder, TourReq("DELETE", HttpVerb.DELETE, "{{host}}/delete"));
        Add(folder, TourReq("Anything", HttpVerb.GET, "{{host}}/anything?from=feature-tour"));
        return folder;
    }

    private static FolderDefinition BuildVariablesFolder()
    {
        var folder = new FolderDefinition { Name = "Variables" };

        var inherited = new FolderDefinition { Name = "Inherited" };
        Add(inherited, TourReq(
            "Echo inherited client and region",
            HttpVerb.GET,
            "{{host}}/get?client={{client}}&region={{region}}"));
        Add(folder, inherited);

        var overrideRegion = new FolderDefinition { Name = "Override" };
        overrideRegion.Variables.Add(Var("region", "eu"));
        Add(overrideRegion, TourReq(
            "Echo — region is eu, client still inherited",
            HttpVerb.GET,
            "{{host}}/get?client={{client}}&region={{region}}"));
        var nested = new FolderDefinition { Name = "Still overridden here" };
        Add(nested, TourReq(
            "Echo nested — region stays eu",
            HttpVerb.POST,
            "{{host}}/post",
            body: """{"client":"{{client}}","region":"{{region}}"}""",
            contentType: "application/json"));
        Add(overrideRegion, nested);
        Add(folder, overrideRegion);

        var calculated = new FolderDefinition { Name = "Calculated" };
        calculated.Variables.Add(Var("today", "@date(today)"));
        calculated.Variables.Add(Var("now", "@date(now)"));
        calculated.Variables.Add(Var("weekAgo", "@date(today-7d)"));
        calculated.Variables.Add(Var("label", "signed {{today}}"));
        Add(calculated, TourReq(
            "Echo date recipes and nested label",
            HttpVerb.GET,
            "{{host}}/get?today={{today}}&now={{now}}&weekAgo={{weekAgo}}&label={{label}}"));
        Add(folder, calculated);

        return folder;
    }

    private static FolderDefinition BuildMovementFolder()
    {
        var folder = new FolderDefinition { Name = "Node movement" };

        var dropTarget = new FolderDefinition { Name = "Drop target (Alt+Right)" };
        Add(dropTarget, TourReq("Already inside the drop target", HttpVerb.GET, "{{host}}/get"));
        Add(folder, dropTarget);

        Add(folder, TourReq("Alt+Right — indent into Drop target above", HttpVerb.GET, "{{host}}/get"));
        Add(folder, TourReq("Alt+Up — swap with the request above", HttpVerb.GET, "{{host}}/get"));
        Add(folder, TourReq("Alt+Down — swap with the request below", HttpVerb.GET, "{{host}}/get"));
        Add(folder, TourReq("Alt+Shift+Right — wrap me in a new folder", HttpVerb.GET, "{{host}}/get"));

        var nested = new FolderDefinition { Name = "Nested (Alt+Left to leave)" };
        Add(nested, TourReq("Alt+Left — outdent to Node movement", HttpVerb.GET, "{{host}}/get"));
        Add(folder, nested);

        return folder;
    }

    private static FolderDefinition BuildExportingFolder()
    {
        var folder = new FolderDefinition { Name = "Exporting" };

        var postmanReady = new FolderDefinition { Name = "Postman-ready" };
        postmanReady.Variables.Add(Var("host", "https://httpbin.org"));
        postmanReady.Variables.Add(Var("env", "shared"));
        var alpha = new FolderDefinition { Name = "Alpha" };
        Add(alpha, TourReq("Alpha echo", HttpVerb.GET, "{{host}}/get?env={{env}}"));
        var beta = new FolderDefinition { Name = "Beta" };
        Add(beta, TourReq("Beta echo", HttpVerb.GET, "{{host}}/get?env={{env}}"));
        Add(postmanReady, alpha);
        Add(postmanReady, beta);
        Add(folder, postmanReady);

        var flattenRequired = new FolderDefinition { Name = "Flatten required" };
        flattenRequired.Variables.Add(Var("host", "https://httpbin.org"));
        var staging = new FolderDefinition { Name = "Staging" };
        staging.Variables.Add(Var("env", "staging"));
        Add(staging, TourReq("Staging echo", HttpVerb.GET, "{{host}}/get?env={{env}}"));
        var production = new FolderDefinition { Name = "Production" };
        production.Variables.Add(Var("env", "production"));
        Add(production, TourReq("Production echo", HttpVerb.GET, "{{host}}/get?env={{env}}"));
        Add(flattenRequired, staging);
        Add(flattenRequired, production);
        Add(folder, flattenRequired);

        return folder;
    }

    private static void Add(CollectionDefinition parent, FolderDefinition folder)
    {
        parent.Folders.Add(folder);
        parent.ChildOrder.Add(new ChildOrderEntry { Kind = TreeChildKind.Folder, Id = folder.Id });
    }

    private static void Add(FolderDefinition parent, FolderDefinition folder)
    {
        parent.Folders.Add(folder);
        parent.ChildOrder.Add(new ChildOrderEntry { Kind = TreeChildKind.Folder, Id = folder.Id });
    }

    private static void Add(FolderDefinition parent, RequestDefinition request)
    {
        parent.Requests.Add(request);
        parent.ChildOrder.Add(new ChildOrderEntry { Kind = TreeChildKind.Request, Id = request.Id });
    }

    private static KeyValueItem Var(string key, string value) =>
        new() { Key = key, Value = value, IsEnabled = true };

    private static RequestDefinition TourReq(
        string name,
        HttpVerb verb,
        string address,
        string? body = null,
        string? contentType = null)
    {
        var request = Req(name, verb, address, body, contentType);
        request.ExpectTimingEnabled = false;
        return request;
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
