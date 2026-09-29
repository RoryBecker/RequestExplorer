# RequestExplorer

Windows desktop HTTP client (C# / WPF). See [PLAN.md](PLAN.md) for the multiphase roadmap.

## Requirements

- .NET 8 SDK (Windows Desktop / WPF)
- Windows 10 or 11

## Build & run

```powershell
dotnet build RequestExplorer.sln
dotnet run --project RequestExplorer
```

## Persistence

- One JSON file per collection under `%LocalAppData%\RequestExplorer\collections\`
- File name: `{collectionId}.json` (GUID, no dashes)
- **Ctrl+S** saves the collection that contains the current selection (or all collections if nothing is selected)
- **Ctrl+Shift+S** saves all collections
- First launch with an empty store seeds the httpbin demo and writes it to disk

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
      "authentication": { "type": "none", "bearerToken": "", "…": "…" },
      "body": { "content": "", "contentType": "application/json" }
    }
  ]
}
```

## Phase status

- Phase 0: scaffold (split main window)
- Phase 1: domain model + collections tree + seed data + basic CRUD
- Phase 2: RHS request/response shell (selection switching)
- Phase 3: editable request fields + dirty `*` tracking
- Phase 4: Ctrl+S persistence (one JSON file per collection)
- Phase 5: Send / Cancel + response panel (status, body, headers, cookies, timing)
- Later: polish (Phase 6)
