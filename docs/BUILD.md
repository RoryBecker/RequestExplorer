# Build and run

RequestExplorer is a Windows WPF app targeting .NET 8.

## Requirements

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (includes Windows Desktop / WPF)

## Build & run

From the repository root:

```powershell
dotnet build RequestExplorer.sln
dotnet run --project RequestExplorer
```

First launch with an empty workspace seeds a **Feature Tour** collection and selects it so the description on the right explains how to use each folder. **File → Add Demo Collections** adds the same collection again.

## Optional publish

Framework-dependent (requires the .NET 8 Desktop runtime):

```powershell
dotnet publish RequestExplorer -c Release -r win-x64 --self-contained false -o .\publish
```

Self-contained (larger; no shared runtime required):

```powershell
dotnet publish RequestExplorer -c Release -r win-x64 --self-contained true -o .\publish-sc
```
