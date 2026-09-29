# RequestExplorer

A local Windows HTTP client (C# / WPF) for composing and sending requests, organized in Collections → Folders → Requests.

See [PLAN.md](PLAN.md) for the full multiphase roadmap and locked product decisions.

## Requirements

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (includes Windows Desktop / WPF)

## Build & run

```powershell
dotnet build RequestExplorer.sln
dotnet run --project RequestExplorer
```

### Optional publish (unpackaged)

Framework-dependent (smaller; requires .NET 8 Desktop runtime on the machine):

```powershell
dotnet publish RequestExplorer -c Release -r win-x64 --self-contained false -o .\publish
```

Self-contained single-folder (larger; no shared runtime required):

```powershell
dotnet publish RequestExplorer -c Release -r win-x64 --self-contained true -o .\publish-sc
```

MSIX / Store packaging is deferred (preference **P4**).

## Persistence

- One JSON file per collection under `%LocalAppData%\RequestExplorer\collections\`
- File name: `{collectionId}.json` (GUID without dashes)
- First launch with an empty store seeds the httpbin demo and writes it to disk
- **File → Open collections folder** reveals the storage directory

### Collection JSON shape (camelCase)

```json
{
  "id": "…",
  "name": "httpbin.org Demo",
  "folders": [ { "id": "…", "name": "…", "folders": [], "requests": [] } ],
  "requests": [
    {
      "id": "…",
      "name": "GET",
      "verb": "get",
      "address": "https://httpbin.org/get",
      "params": [ { "isEnabled": true, "key": "q", "value": "x" } ],
      "headers": [],
      "authentication": { "type": "none" },
      "body": { "content": "", "contentType": "application/json" }
    }
  ]
}
```

## Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+S | Save current collection |
| Ctrl+Shift+S | Save all collections |
| Ctrl+Enter | Send selected request |
| Ctrl+Shift+N | New collection |
| Ctrl+D | Duplicate selected request |
| F2 | Rename |
| Del | Delete |
| Esc (dialog) | Cancel prompt |

## Features

- Explorer-style tree with collections, folders, and requests
- Editable verb, URL, params, headers, auth (None / Bearer / Basic / API Key), raw/JSON body
- Dirty tracking with `*` on requests and ancestor nodes; Ctrl+S to persist
- Send / Cancel with response status, body, headers, cookies, and timing
- Build a collection from scratch via toolbar, context menu, or **Edit** menu

## Phase status

All planned phases (0–6) are implemented. Further work is optional enhancement beyond the original plan.
