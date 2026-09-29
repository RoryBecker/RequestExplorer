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

## Phase status

- Phase 0: scaffold (split main window)
- Phase 1: domain model + collections tree + seed data + basic CRUD
- Phase 2: RHS request/response shell (selection switching)
- Phase 3: editable request fields + dirty `*` tracking
- Later: Ctrl+S persistence, send/response

