[← docs index](../README.md)

# Commands

This page is for AI coding agents making changes to the Obsidian Quick Note Widget repo. Copy-paste blocks, Windows + PowerShell. Run from the repo root unless stated otherwise.

## Build

Restore + build Debug:

```powershell
dotnet restore ObsidianQuickNoteWidget.slnx
dotnet build   ObsidianQuickNoteWidget.slnx -c Debug --nologo
```

Build Release:

```powershell
dotnet build ObsidianQuickNoteWidget.slnx -c Release --nologo
```

## Test

Full suite (Release — matches CI expectations; 403 tests at HEAD):

```powershell
dotnet test tests\ObsidianQuickNoteWidget.Core.Tests\ObsidianQuickNoteWidget.Core.Tests.csproj -c Release --nologo
```

Single test (xUnit filter — matches `FullyQualifiedName` or `DisplayName`):

```powershell
dotnet test tests\ObsidianQuickNoteWidget.Core.Tests\ObsidianQuickNoteWidget.Core.Tests.csproj `
  --filter "FullyQualifiedName~CardDataBuilderTests.BuildQuickNoteData_SeedsFolderNew" --nologo
```

Test class:

```powershell
dotnet test tests\ObsidianQuickNoteWidget.Core.Tests\ObsidianQuickNoteWidget.Core.Tests.csproj `
  --filter "FullyQualifiedName~PerWidgetGateTests" --nologo
```

Fast (no rebuild, requires a prior build):

```powershell
dotnet test tests\ObsidianQuickNoteWidget.Core.Tests\ObsidianQuickNoteWidget.Core.Tests.csproj --no-build --nologo
```

## Format

```powershell
dotnet format ObsidianQuickNoteWidget.slnx --verify-no-changes
```

`dotnet format ObsidianQuickNoteWidget.slnx` without `--verify-no-changes` applies fixes in place.

## Full deploy pipeline (local sideload)

One-time machine setup: dev cert + per-user password written under `%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\`:

```powershell
.\tools\New-DevCert.ps1
```

### 1. Pack signed MSIX

```powershell
dotnet publish src\ObsidianQuickNoteWidget\ObsidianQuickNoteWidget.csproj -c Release -p:Platform=x64 `
  -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false `
  -p:AppxBundle=Always -p:UapAppxPackageBuildMode=SideloadOnly
```

The unsigned MSIX lands under `src\ObsidianQuickNoteWidget\bin\x64\Release\AppPackages\…\ObsidianQuickNoteWidget_*.msix`.

### 2. Sign with dev cert

`Sign-DevMsix.ps1` reads the password from `%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\password.txt` (user-only ACL):

```powershell
$msix = Get-ChildItem -Recurse -Filter 'ObsidianQuickNoteWidget_*.msix' `
  src\ObsidianQuickNoteWidget\bin\x64\Release\AppPackages | Sort-Object LastWriteTime | Select-Object -Last 1
.\tools\Sign-DevMsix.ps1 $msix.FullName
```

### 3. Kick the host + install

Kill cached Widget Host processes **before** install — the Widget Host caches the AppExtension catalog and stale metadata survives `Add-AppxPackage -Force*`:

```powershell
Get-Process Widgets, WidgetService, WebExperienceHost, dasHost, ObsidianQuickNoteWidget -ErrorAction SilentlyContinue |
  Stop-Process -Force
Add-AppxPackage -Path $msix.FullName
```

For a fully clean reinstall after manifest shape changes (definitions, sizes, CLSID):

```powershell
Get-AppxPackage ObsidianQuickNoteWidget | Remove-AppxPackage
# then repeat the install above
```

### 4. Tail the packaged log

```powershell
Get-ChildItem "$env:LocalAppData\Packages\ObsidianQuickNoteWidget_*\LocalCache\Local" -Recurse -Filter log.txt |
  Sort-Object LastWriteTime | Select-Object -Last 1 |
  ForEach-Object { Get-Content -Wait $_.FullName }
```

## Probe the live Obsidian CLI

Obsidian 1.12+ with the CLI registered (Settings → General → Command Line Interface → Register CLI). These round-trip the exact calls the widget makes:

```powershell
obsidian vault info=path
# → C:\Users\<you>\Documents\ObsidianVault

obsidian vault
# → name<TAB>ObsidianVault
#   path<TAB>C:\Users\<you>\Documents\ObsidianVault
#   files<TAB>123
#   folders<TAB>45
#   size<TAB>987654

obsidian folders
# → /
#   Daily
#   Notes
#   Projects/Active
#   (forward-slash separated; vault root is `/`)

obsidian files
# → Daily/2026-04-19.md
#   Notes/ideas.md
#   …
#   (every file in the vault, one per line)

obsidian recents
# → Notes/ideas.md
#   Daily/2026-04-19.md
#   … (up to 10; may include ghost entries for deleted files)

obsidian create name="Probe" path="Notes" content="hello\nworld"
# → created: Notes/Probe.md
# (error example — note exit code 0 and `Error:` stdout prefix:)
# → Error: Vault is not open
```

`ObsidianCliParsers.HasCliError(stdout)` is the authoritative failure detector.

## Probe the AppExtension registration (bypasses Widget Host cache)

```powershell
dotnet run --project tools\AppExtProbe\AppExtProbe.csproj -c Release
```

Expected: one entry per widget definition (`ObsidianQuickNote`, `ObsidianRecentNotes`, `PluginRunner`) with the matching `PackageFamilyName`.

## Git commit (agent-authored)

The `Co-authored-by: Copilot` trailer is **mandatory** for agent-authored commits.

```powershell
git add <paths>
git commit -m "scope(area): short imperative summary

Optional body.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

One-liner for docs-only changes:

```powershell
git commit -m "docs(agents): add AI agent onboarding + conventions + commands" `
           -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```
