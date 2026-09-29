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

Phase 0 scaffold is in place (split main window). Later phases add the collection tree, request editor, save, and send/response.
