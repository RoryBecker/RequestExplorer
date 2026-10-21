# RequestExplorer

A local Windows HTTP client for building, organizing, and testing API requests. Work is arranged in a **Collections → Folders → Requests** tree, with variables, expectations, batch runs, and Postman import/export—all stored as JSON on your machine.

## Requirements

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (includes Windows Desktop / WPF)

## Build & run

```powershell
dotnet build RequestExplorer.sln
dotnet run --project RequestExplorer
```

First launch with an empty workspace seeds a small **httpbin.org Demo** collection (HTTP methods, auth, XML/SOAP) so you can try Send immediately.

### Optional publish

Framework-dependent (requires the .NET 8 Desktop runtime):

```powershell
dotnet publish RequestExplorer -c Release -r win-x64 --self-contained false -o .\publish
```

Self-contained (larger; no shared runtime required):

```powershell
dotnet publish RequestExplorer -c Release -r win-x64 --self-contained true -o .\publish-sc
```

## Features

### Collections tree

- Organize requests in collections and nested folders
- Create, rename, duplicate, and delete nodes from the toolbar, **Edit** menu, or tree context menu
- Multi-select for batch operations
- Drag and drop to reorder or nest (before / after / into)
- Move up/down with **Alt+↑** / **Alt+↓**, or **Move to New Sub-folder…**
- **Copy to Collection** — copy the selection into an existing collection or a newly named one
- Tree filter with optional matching on name, URL, and ancestor path
- **Edit as JSON…** for a collection, folder, or request subtree

### Editing & sending requests

- Verbs: GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS
- URL, raw body (with content type), query params, headers (per-row on/off), and auth (None, Bearer, Basic, API Key)
- **Send** (**Alt+X**) and **Cancel** for in-flight requests
- Unsaved changes show `*` on the request and ancestors — **Ctrl+S** saves the current collection

### Response pane

- Status, elapsed time, and body with find next/previous
- Headers, cookies, raw response, and timing breakdown
- Save the response body to a file when available

### Variables

- Define variables on collections and folders; children inherit them (with source and shadowing shown)
- Use `{{name}}` in URLs and other request fields
- **Diagnose Variables** on a selection to find missing or disabled variables and jump to the affected nodes
- Variable **values** may be dynamic recipes (see below); the stored value stays the recipe, and send/tooltips use the computed result
- Hover the **name** (key) of a variable in any Variables grid — including **Inherited** — to see a tooltip with the current calculated value (e.g. resolved `@date(…)` recipes). The same hover works on Params/Headers keys (shows that row’s value). Hovering `{{name}}` tokens in URL/body/auth fields shows each token’s resolved value.

#### Dynamic date values

Set a variable’s **value** to a recipe instead of a fixed string. Reference it as usual with `{{name}}`. Default zone is **UTC**.

| Value (recipe) | Meaning |
|----------------|---------|
| `@date(today)` | UTC calendar date today (`yyyy-MM-dd`) |
| `@date(today-7d)` | UTC date seven days ago |
| `@date(today+3d)` | UTC date three days ahead |
| `@date(today-7d, yyyyMMdd)` | Same offset with a custom [.NET date format](https://learn.microsoft.com/dotnet/standard/base-types/custom-date-and-time-format-strings) |
| `@date(today, local)` | Local calendar date |
| `@date(now)` | UTC date-time (`yyyy-MM-ddTHH:mm:ssZ`) |
| `@date(now-1d)` | UTC instant one day earlier |
| `@date(now, local)` | Local date-time (no `Z` by default) |

Example: key `weekAgo`, value `@date(today-7d)`, then use `{{weekAgo}}` in the URL or body.

Notes:

- Offsets are whole days only (`±Nd`).
- Invalid recipes are left unchanged (the literal `@date(…)` text is kept).

Full reference: [docs/VARIABLES.md](docs/VARIABLES.md).

### Expectations & Run All

- Per request: expected status code; optional max response time and exact body match
- **Run All** runs every request under the selected collection, folder, or single request. Folders/requests can **Opt out of auto run** (they still appear in the results as opted out; Diagnose Variables still includes them)
- Results dialog: parallel threads (1–10), optional timing checks, sortable grid with Response / Body / Time / Met highlighting, stop remaining, and per-row rerun
- Double-click a result row to select that request in the tree

### Quick Export

- Side panel with live previews for cURL, HTTP, Postman Collection v2.1, JavaScript Fetch, PowerShell, Python requests, HTTPie, and more
- Copy to clipboard; diagnosis when variables or the URL cannot be resolved cleanly

### Postman import & export

From a collection or folder **…** menu:

- **Export to Postman…**
- **Export to Postman (Flatten)…** (variables inlined)
- **Import Postman Collection(s)…** into that container
- **Import from OpenAPI JSON…** — paste OpenAPI 3.x / Swagger 2.0 JSON; creates a folder under the selected collection/folder, or a new top-level collection when opened on empty tree space

### Undo, backups & settings

- Undo / redo (**Ctrl+Z** / **Ctrl+Y**), including multi-step history from the toolbar
- **File → Backup now**, **Restore…**, and **Open backups folder**
- **Tools → Settings…**: request timeout, optional “dev mode” (no timeout; Cancel still works), backup on startup, and how many backups to keep
- Layout, filters, dark mode, and Run All options are remembered between sessions
- Toolbar toggle for **dark mode** (moon icon)

## Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+S | Save current collection |
| Ctrl+Shift+S | Save all collections |
| Alt+X | Send selected request |
| Ctrl+N | New… (collection / folder / request) |
| Ctrl+Shift+N | New collection |
| Ctrl+D | Duplicate selected node |
| Ctrl+C / Ctrl+V | Copy / paste tree nodes |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| F2 | Rename |
| Ctrl+T | Focus selected tree node |
| Del | Delete |
| Alt+↑ / Alt+↓ | Move among siblings |
| Esc | Clear text selection, or focus selected tree node |

## Where your data lives

| Path | Contents |
|------|----------|
| `%LocalAppData%\RequestExplorer\collections\` | One JSON file per collection (named after the collection; id suffix if needed), plus `collection-order.json` |
| `%LocalAppData%\RequestExplorer\preferences.json` | UI preferences (layout, filters, Run All options, etc.) |
| `%LocalAppData%\RequestExplorer\backups\` | Rolling zip backups |

Use **File → Open collections folder** to open the collections directory in Explorer.
