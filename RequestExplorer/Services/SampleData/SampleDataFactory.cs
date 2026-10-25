using RequestExplorer.Models;
using RequestExplorer.Services.Tree;
using RequestExplorer.ViewModels;
using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.SampleData;

public static class SampleDataFactory
{
    public static IReadOnlyList<CollectionDefinition> CreateDemoDefinitions() =>
        [BuildFeatureTourCollection()];

    public static IReadOnlyList<CollectionNodeViewModel> CreateDemoWorkspace() =>
        CreateDemoDefinitions().Select(CollectionTreeMapper.ToViewModel).ToList();

    /// <summary>
    /// Walkthrough collection: send, variables, tree moves, and Postman export vs flatten.
    /// </summary>
    private static CollectionDefinition BuildFeatureTourCollection()
    {
        var collection = new CollectionDefinition
        {
            Name = "Feature Tour",
            Description = """
                Walk through Request Explorer using the folders below. Select a folder to read how that feature works, then try the requests inside it.

                Collection variables (host, client, region) are inherited by every request unless a closer folder overrides them.
                """
        };
        collection.Variables.Add(Var("host", "https://httpbin.org"));
        collection.Variables.Add(Var("client", "RequestExplorer"));
        collection.Variables.Add(Var("region", "global"));

        Add(collection, BuildSendFolder());
        Add(collection, BuildDiagnosisFolder());
        Add(collection, BuildVariablesFolder());
        Add(collection, BuildMovementFolder());
        Add(collection, BuildExportingFolder());
        return collection;
    }

    private static FolderDefinition BuildSendFolder()
    {
        var folder = new FolderDefinition
        {
            Name = "Simple requests",
            Description = """
                Select a request and click Send (or press Alt+X).

                GET and DELETE have no body. POST, PUT, and PATCH send JSON — open the Body tab to edit it, then Send and inspect the response.
                """
        };
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

    private static FolderDefinition BuildDiagnosisFolder()
    {
        var folder = new FolderDefinition
        {
            Name = "Right Click - Run All Requests",
            Description = """
                Right-click this folder and choose Run All Requests.

                That sends every request under the folder (unless opted out). Use the child folders to see status codes, expectation pass/fail, and skip behaviour.
                """
        };

        var status = new FolderDefinition
        {
            Name = "Status codes",
            Description = """
                Each request expects the HTTP status in its name. Right-click this folder → Run All Requests.

                2xx, 4xx, and 5xx all pass when the expected code matches the response.
                """
        };
        Add(status, TourReq("200 OK", HttpVerb.GET, "{{host}}/status/200", expectedStatus: 200));
        Add(status, TourReq("201 Created", HttpVerb.GET, "{{host}}/status/201", expectedStatus: 201));
        Add(status, TourReq("204 No Content", HttpVerb.GET, "{{host}}/status/204", expectedStatus: 204));
        Add(status, TourReq("400 Bad Request", HttpVerb.GET, "{{host}}/status/400", expectedStatus: 400));
        Add(status, TourReq("401 Unauthorized", HttpVerb.GET, "{{host}}/status/401", expectedStatus: 401));
        Add(status, TourReq("403 Forbidden", HttpVerb.GET, "{{host}}/status/403", expectedStatus: 403));
        Add(status, TourReq("404 Not Found", HttpVerb.GET, "{{host}}/status/404", expectedStatus: 404));
        Add(status, TourReq("418 I'm a teapot", HttpVerb.GET, "{{host}}/status/418", expectedStatus: 418));
        Add(status, TourReq("500 Server Error", HttpVerb.GET, "{{host}}/status/500", expectedStatus: 500));
        Add(status, TourReq("503 Unavailable", HttpVerb.GET, "{{host}}/status/503", expectedStatus: 503));
        Add(folder, status);

        var expectations = new FolderDefinition
        {
            Name = "Expectations",
            Description = """
                Open a request and look at Expectations (status, body, timing). Green means met; red means failed.

                Right-click this folder → Run All Requests to see mixed results in one pass.
                """
        };
        Add(expectations, TourReq(
            "Met — status 200 as expected",
            HttpVerb.GET,
            "{{host}}/status/200",
            expectedStatus: 200));
        Add(expectations, TourReq(
            "Fail — expect 200, actually 404",
            HttpVerb.GET,
            "{{host}}/status/404",
            expectedStatus: 200));
        Add(expectations, TourReq(
            "Met — body contains marker",
            HttpVerb.POST,
            "{{host}}/post",
            body: """{"demo":"feature-tour"}""",
            contentType: "application/json",
            expectBody: true,
            expectBodyContains: true,
            expectedBody: "feature-tour"));
        Add(expectations, TourReq(
            "Fail — body exact match",
            HttpVerb.GET,
            "{{host}}/get",
            expectBody: true,
            expectBodyContains: false,
            expectedBody: "(this will not match)"));
        Add(expectations, TourReq(
            "Met — timing under 10s",
            HttpVerb.GET,
            "{{host}}/get",
            expectTiming: true,
            expectedTimingMs: 10000));
        Add(expectations, TourReq(
            "Fail — timing under 1ms",
            HttpVerb.GET,
            "{{host}}/get",
            expectTiming: true,
            expectedTimingMs: 1));
        Add(folder, expectations);

        var optedOut = new FolderDefinition
        {
            Name = "Opted out",
            Description = """
                Right-click this folder → Run All Requests.

                "Runs in Run All" is sent. The opted-out request and everything in "Opted-out folder" are skipped. Diagnose Variables still includes opted-out nodes.
                """
        };
        Add(optedOut, TourReq("Runs in Run All", HttpVerb.GET, "{{host}}/get"));
        Add(optedOut, TourReq(
            "Opted-out request",
            HttpVerb.GET,
            "{{host}}/get",
            optOut: true));
        var optedFolder = new FolderDefinition
        {
            Name = "Opted-out folder",
            OptOutOfAutoRun = true,
            Description = """
                This folder is opted out of auto run (checkbox in the header). Right-click a parent and Run All Requests — the request inside is skipped with the folder.
                """
        };
        Add(optedFolder, TourReq("Skipped with the folder", HttpVerb.GET, "{{host}}/get"));
        Add(optedOut, optedFolder);
        Add(folder, optedOut);

        return folder;
    }

    private static FolderDefinition BuildVariablesFolder()
    {
        var folder = new FolderDefinition
        {
            Name = "Variables",
            Description = """
                {{name}} resolves from the nearest ancestor that defines it.

                This collection sets host, client, and region. Open Inherited (no locals), Override (region=eu), then Calculated (@date recipes).
                """
        };

        var inherited = new FolderDefinition
        {
            Name = "Inherited",
            Description = """
                This folder defines no variables. Send the request (Alt+X) and check the query string — client and region come from Feature Tour.
                """
        };
        Add(inherited, TourReq(
            "Echo inherited client and region",
            HttpVerb.GET,
            "{{host}}/get?client={{client}}&region={{region}}"));
        Add(folder, inherited);

        var overrideRegion = new FolderDefinition
        {
            Name = "Override",
            Description = """
                This folder sets region=eu. Send the request: region is eu, client is still inherited from the collection. The nested folder keeps that override.
                """
        };
        overrideRegion.Variables.Add(Var("region", "eu"));
        Add(overrideRegion, TourReq(
            "Echo — region is eu, client still inherited",
            HttpVerb.GET,
            "{{host}}/get?client={{client}}&region={{region}}"));
        var nested = new FolderDefinition
        {
            Name = "Still overridden here",
            Description = """
                No local region here, so Override (region=eu) still wins. Send the POST and check the echoed JSON body.
                """
        };
        Add(nested, TourReq(
            "Echo nested — region stays eu",
            HttpVerb.POST,
            "{{host}}/post",
            body: """{"client":"{{client}}","region":"{{region}}"}""",
            contentType: "application/json"));
        Add(overrideRegion, nested);
        Add(folder, overrideRegion);

        var calculated = new FolderDefinition
        {
            Name = "Calculated",
            Description = """
                Local variables use @date(...) recipes. label nests {{today}} inside another value.

                Send the request and compare the resolved query values.
                """
        };
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
        var folder = new FolderDefinition
        {
            Name = "Node Movement",
            Description = """
                Select a node, then use Alt+Arrow keys or drag in the tree.

                Alt+Up / Alt+Down swap with a sibling. Alt+Right moves into the folder above. Alt+Left moves out. Alt+Shift+Right wraps the node in a new folder.
                """
        };

        var dropTarget = new FolderDefinition
        {
            Name = "Drop target (Alt+Right)",
            Description = """
                Select the sibling request below this folder named "Alt+Right — indent into Drop target above" and press Alt+Right to move it in here.
                """
        };
        Add(dropTarget, TourReq("Already inside the drop target", HttpVerb.GET, "{{host}}/get"));
        Add(folder, dropTarget);

        Add(folder, TourReq("Alt+Right — indent into Drop target above", HttpVerb.GET, "{{host}}/get"));
        Add(folder, TourReq("Alt+Up — swap with the request above", HttpVerb.GET, "{{host}}/get"));
        Add(folder, TourReq("Alt+Down — swap with the request below", HttpVerb.GET, "{{host}}/get"));
        Add(folder, TourReq("Alt+Shift+Right — wrap me in a new folder", HttpVerb.GET, "{{host}}/get"));

        var nested = new FolderDefinition
        {
            Name = "Nested (Alt+Left to leave)",
            Description = """
                Select the request inside this folder and press Alt+Left to move it back under Node Movement.
                """
        };
        Add(nested, TourReq("Alt+Left — outdent to Node Movement", HttpVerb.GET, "{{host}}/get"));
        Add(folder, nested);

        return folder;
    }

    private static FolderDefinition BuildExportingFolder()
    {
        var folder = new FolderDefinition
        {
            Name = "Exporting",
            Description = """
                Right-click a folder and export as Postman.

                Postman-ready shares the same env on every branch. Flatten required uses different env values in Staging vs Production — Postman export will report a clash; Flatten still produces a file.
                """
        };

        var postmanReady = new FolderDefinition
        {
            Name = "Postman-ready",
            Description = """
                Alpha and Beta both inherit env=shared. Right-click this folder and export as Postman — it should succeed without a variable clash.
                """
        };
        postmanReady.Variables.Add(Var("host", "https://httpbin.org"));
        postmanReady.Variables.Add(Var("env", "shared"));
        var alpha = new FolderDefinition
        {
            Name = "Alpha",
            Description = "Inherits env=shared from Postman-ready. Export the parent folder as Postman."
        };
        Add(alpha, TourReq("Alpha echo", HttpVerb.GET, "{{host}}/get?env={{env}}"));
        var beta = new FolderDefinition
        {
            Name = "Beta",
            Description = "Same env as Alpha. Shared values across sibling folders are what Postman export can hoist."
        };
        Add(beta, TourReq("Beta echo", HttpVerb.GET, "{{host}}/get?env={{env}}"));
        Add(postmanReady, alpha);
        Add(postmanReady, beta);
        Add(folder, postmanReady);

        var flattenRequired = new FolderDefinition
        {
            Name = "Flatten required",
            Description = """
                Staging sets env=staging; Production sets env=production. Export as Postman to see the clash report, then Flatten to inline each branch's values.
                """
        };
        flattenRequired.Variables.Add(Var("host", "https://httpbin.org"));
        var staging = new FolderDefinition
        {
            Name = "Staging",
            Description = "Defines env=staging. Export Flatten required (the parent) as Postman to compare with Production."
        };
        staging.Variables.Add(Var("env", "staging"));
        Add(staging, TourReq("Staging echo", HttpVerb.GET, "{{host}}/get?env={{env}}"));
        var production = new FolderDefinition
        {
            Name = "Production",
            Description = "Defines env=production — a different value from Staging. That sibling difference is what blocks a nested Postman export."
        };
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
        string? contentType = null,
        int expectedStatus = 200,
        bool optOut = false,
        bool expectBody = false,
        bool expectBodyContains = false,
        string expectedBody = "",
        bool expectTiming = false,
        double expectedTimingMs = 300)
    {
        var request = Req(name, verb, address, body, contentType);
        request.ExpectedStatusCode = expectedStatus;
        request.OptOutOfAutoRun = optOut;
        request.ExpectBodyEnabled = expectBody;
        request.ExpectBodyContains = expectBodyContains;
        request.ExpectedBody = expectedBody;
        request.ExpectTimingEnabled = expectTiming;
        if (expectTiming)
        {
            request.ExpectedTimingMilliseconds = expectedTimingMs;
        }

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
