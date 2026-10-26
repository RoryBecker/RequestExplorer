# RequestExplorer

A local Windows HTTP client for building, organizing, and testing API requests. Work is arranged in a **Collections → Folders → Requests** tree, with variables, expectations, batch runs, and Postman import/export—all stored as JSON on your machine.

## Headline features

Where RequestExplorer is strong for day-to-day local API work:

- **Your files, no account.** Collections are ordinary JSON under `%LocalAppData%`. Copy, zip, git, or drop them onto another machine. There is no cloud workspace and nothing to sign in to.
- **The tree is an editor.** Indent and outdent with **Alt+← / Alt+→**, wrap a node in a new folder, drag before/after/into, insert a folder above. Postman’s collection sidebar is a navigator; this tree is how you structure work.
- **Folder variables that stay where you put them.** Children inherit; closer folders override; the Inherited grid shows the source and what is shadowed. Postman export hoists variables to the collection — RequestExplorer warns when sibling folders would clash, and can **Flatten** if you still want a Postman file.
- **See what `{{name}}` will become.** Calculated URL with bold substitutions, Diagnose Variables on a subtree, hover tokens for resolved values, Go to Definition, and `@date(…)` recipes without pre-request scripts. Apply a variable into matching URLs — one request, or every descendant that still has the literal value.
- **Undo is real.** Tree moves, edits, and imports sit on **Ctrl+Z / Ctrl+Y**, including multi-step history. You can also **Edit as JSON…** on any collection, folder, or request and apply the native model back.
- **Run All with expectations, no test scripts.** Expected status, body (exact or contains), and timing. Opt folders or requests out of the batch and still diagnose them. Results are a local grid you can sort, stop, and rerun.
- **Quick Export beside the request.** Live cURL, HTTP, Fetch, PowerShell, Python, HTTPie, and Postman snippets, with a diagnosis when variables will not resolve.
- **Built for Windows.** Keyboard-first WPF, dark mode, rolling zip backups, and optional Dirty Save so unsaved work can come back after a restart.

Postman remains a useful interchange format. RequestExplorer imports and exports Collection v2.1 so you can leave or return without being locked in. RequestExplorer-only fields (descriptions, expectations, opt-out, and similar) may not survive a Postman round-trip.

## Getting started

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows 10 or 11, then [build and run](docs/BUILD.md).

On first launch with an empty workspace, **Feature Tour** is created and selected. Read the description on the right, then work through the folders (send, Run All, variables, node movement, Postman export). **File → Add Demo Collections** adds that collection again.

## Features

### Collections tree

- Organize requests in collections and nested folders
- Multiline **Description** on collections, folders, and requests (the first tab in each editor; Variables is next on collections and folders)
- Create, rename, duplicate, and delete nodes from the toolbar, **Edit** menu, or tree context menu
- **Insert Folder Above** (**Ins**)
- Multi-select for batch operations
- Drag and drop to reorder or nest (before / after / into)
- Move among siblings with **Alt+↑** / **Alt+↓**; **Alt+→** moves in, **Alt+←** moves out; **Alt+Shift+→** wraps in a new folder
- **Copy** / **Paste** (including **Paste at Top Level**) uses native RequestExplorer JSON on the clipboard, so another running copy can paste the same nodes
- **Copy to Collection** — copy the selection into an existing collection or a newly named one
- Tree filter with optional matching on name, URL, and ancestor path
- **Edit as JSON…** for a collection, folder, or request subtree
- Drop Postman or RequestExplorer `.json` collection files onto the tree to import them as top-level collections

### Editing & sending requests

- Verbs: GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS
- URL, raw body (with content type), query params, headers (per-row on/off), and auth (None, Bearer, Basic, API Key)
- Under the URL, a **calculated address** shows `{{variables}}` resolved from parent scope; substituted slices are bold
- **Send** (**Alt+X**) and **Cancel** for in-flight requests
- Unsaved changes show `*` on the request and ancestors — **Ctrl+S** saves the current collection

### Response pane

- Status, elapsed time, and body with find next/previous
- Headers, cookies, raw response, and timing breakdown
- Save the response body to a file when available

### Variables

- Define variables on collections and folders; children inherit them (with source and shadowing shown). Names are **case-insensitive**; nearest enabled ancestor wins. Tick **Secret** to omit the value from Postman export (local save keeps it)
- Use `{{name}}` in URLs and other request fields; a variable value may itself contain `{{name}}` (nesting depth is set in Settings)
- **Diagnose Variables** on a selection to find missing or disabled variables and jump to the affected nodes
- **Go to Definition** (or **Ctrl+click** a `{{name}}` token) opens the defining Variables row. On an unresolved token, **Define Variable…** creates it on the nearest container. Works in the URL, params/headers/variables grids, and Auth Bearer
- Hover the **name** (key) of a variable in any Variables grid — including **Inherited** — to see a tooltip with the current calculated value (e.g. resolved `@date(…)` recipes). The same hover works on Params/Headers keys (shows that row’s value). Hovering `{{name}}` tokens in URL/body/auth fields shows each token’s resolved value
- Dynamic recipes (`@date(…)`): [Variables - Dynamic](docs/VARIABLES-DYNAMIC.md)
- Extract / replace / apply tokens into URLs: [Variables - Refactoring](docs/VARIABLES-REFACTORING.md)

### Expectations & Run All

- Per request: expected status code; optional max response time; optional body check (exact match or contains)
- **Run All** runs every request under the selected collection, folder, or single request. Folders/requests can **Opt out of auto run** (they still appear in the results as opted out; Diagnose Variables still includes them)
- Results dialog: parallel threads (1–10), optional timing checks, sortable grid with Response / Body / Time / Met highlighting, stop remaining, and per-row rerun
- Double-click a result row to select that request in the tree

### Quick Export

- Side panel with live previews for cURL, cURL (Windows), HTTP, Postman Collection v2.1, JavaScript Fetch, PowerShell, Python requests, and HTTPie
- Copy to clipboard; diagnosis when variables or the URL cannot be resolved cleanly

### Import & export

There is no separate “Export as RequestExplorer” command. Native collection JSON is the on-disk format and the copy/paste payload.

From a collection or folder context menu (**Import / Export**):

- **To Postman…** — nested Postman Collection v2.1. If sibling folders would hoist the same variable name with different values, a clash report is shown; you can **Flatten** from that dialog
- **To Postman (Flatten)…** — variables inlined into each request
- **From Postman Collection(s)…** — import as a folder under the selected container
- **From OpenAPI JSON…** — paste OpenAPI 3.x / Swagger 2.0 JSON; creates a folder under the selected collection/folder, or a new top-level collection when opened on empty tree space

Dropping `.json` files onto the tree imports each file as a **top-level collection**. Format is detected from the name and contents (Postman vs RequestExplorer native). You can also copy a saved file from **File → Open collections folder** into another machine’s collections folder.

Postman round-trip keeps the tree and variables; RequestExplorer-only fields (description, expectations, opt-out, and similar) may be lost.

### Undo, backups & settings

- Undo / redo (**Ctrl+Z** / **Ctrl+Y**), including multi-step history from the toolbar
- **File → Reload from Disk**, **Backup now**, **Restore…**, and **Open backups folder**
- **Tools → Settings…**
  - **General:** request timeout, optional “dev mode” (no timeout; Cancel still works), variable nesting depth, mouse back/forward for tree history, backup on startup, and how many backups to keep
  - **Experimental:** **Dirty Save** (off by default) — restore unsaved collections and undo history on the next launch; a Dirty Save button appears when you close with unsaved changes
- Layout, filters, dark mode, editor tabs, and Run All options are remembered between sessions
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
| Ins | Insert folder above |
| Del | Delete |
| Alt+↑ / Alt+↓ | Move among siblings |
| Alt+← / Alt+→ | Move out / in |
| Alt+Shift+→ | Move to new sub-folder |
| Esc | Clear text selection, or focus selected tree node |
| Ctrl+click `{{name}}` | Go to that variable’s definition |

## Where your data lives

| Path | Contents |
|------|----------|
| `%LocalAppData%\RequestExplorer\collections\` | One JSON file per collection (named after the collection; id suffix if needed), plus `collection-order.json` |
| `%LocalAppData%\RequestExplorer\preferences.json` | UI preferences (layout, filters, Run All options, etc.) |
| `%LocalAppData%\RequestExplorer\backups\` | Rolling zip backups |

Use **File → Open collections folder** / **Open backups folder**, or click the paths in **Help → About RequestExplorer**.

## License

Copyright 2026 Rory Becker. RequestExplorer is licensed under the [PolyForm Internal Use License 1.0.0](LICENSE).

You may clone, build, and run it for yourself and for your company’s internal business. You may not distribute or sell the source or binaries. Pull requests are not accepted. Other uses require a separate license from the copyright holder.
