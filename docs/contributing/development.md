# Development setup

> This page is for contributors setting up a local build+run loop against the Widget Board COM server.

Up: [`../README.md`](../README.md) (docs index)

## Prerequisites

| Tool | Version | Notes |
| --- | --- | --- |
| .NET SDK | **10.x** | `Directory.Build.props` pins `TreatWarningsAsErrors=true`; an older SDK will not build the widget csproj. |
| Windows 11 SDK | **10.0.26100** | Matches `TargetFramework=net10.0-windows10.0.26100.0` in [`../../src/ObsidianQuickNoteWidget/ObsidianQuickNoteWidget.csproj`](../../src/ObsidianQuickNoteWidget/ObsidianQuickNoteWidget.csproj). `TargetPlatformMinVersion` is `10.0.22621.0` (Win11 22H2). |
| `signtool.exe` | any | Ships with the Windows SDK; must be on `PATH` for [`../../tools/Sign-DevMsix.ps1`](../../tools/Sign-DevMsix.ps1). |
| Obsidian | **1.12+** with the CLI registered | *Settings → General → Command Line Interface → Enable → Register CLI*. The widget shells out to `obsidian.exe` on `PATH`; `.cmd` / `.bat` PATH entries are rejected (F-02). |
| Developer Mode | on | *Settings → Privacy & security → For developers* — required to sideload an unsigned-by-trusted-root MSIX. |

## Clone and restore

```powershell
git clone <repo-url> obsidian_widget
cd obsidian_widget
dotnet restore ObsidianQuickNoteWidget.slnx
```

## Build

```powershell
dotnet build -c Release
```

Expected: **0 Warning(s), 0 Error(s)**. `TreatWarningsAsErrors=true` is global
(see [`../../Directory.Build.props`](../../Directory.Build.props)) — any warning
fails the build. If something warns, fix the code, not the gate.

## Test

```powershell
dotnet test -c Release
```

Expected at HEAD (`cbce283`, 1.0.0.7): **377 / 377 passed**. Two projects:

- [`../../tests/ObsidianQuickNoteWidget.Core.Tests`](../../tests/ObsidianQuickNoteWidget.Core.Tests) — portable, no Windows APIs.
- [`../../tests/ObsidianQuickNoteWidget.Tests`](../../tests/ObsidianQuickNoteWidget.Tests) — widget COM assembly; needs `x64` and `InternalsVisibleTo` (added in 1.0.0.3).

Run a single test:

```powershell
dotnet test -c Release --filter "FullyQualifiedName~FilenameSanitizerTests.Sanitize_StripsReservedChars"
```

See [`testing.md`](./testing.md) for layout and fakes.

## Format

```powershell
dotnet format --verify-no-changes
```

Exit 0 means clean. `dotnet format` without the flag rewrites in place. Not
wired into CI today; run it locally before pushing.

## Dev cert bootstrap

Sideloaded MSIX installs require the cert to be trusted. First-time setup:

```powershell
.\tools\New-DevCert.ps1
```

[`../../tools/New-DevCert.ps1`](../../tools/New-DevCert.ps1) generates a
self-signed code-signing cert (CN=`ObsidianQuickNoteWidgetDev`, 90-day validity,
SHA256/RSA-2048) and writes three files under
`%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\`:

- `dev.pfx` — the cert.
- `dev.cer` — the public part; import once into **Trusted People** (LocalMachine).
- `password.txt` — a **freshly generated** 24-character random password
  (64-char URL-safe alphabet, `RandomNumberGenerator.GetBytes`), written with
  a user-only ACL (the current user + SYSTEM + Administrators, inheritance
  disabled). The password is never echoed to the console, committed to source,
  or accepted as a CLI argument. This is finding **F-01** (CWE-798) closed —
  see [`../../audit-reports/v3/security-auditor.md`](../../audit-reports/v3/security-auditor.md).

The `dev-cert\` folder and `*.pfx` / `*.cer` are git-ignored. Rotate with
`.\tools\New-DevCert.ps1 -Force`.

Once the `.cer` is in **Trusted People (LocalMachine)** it stays trusted across
re-rotations unless the subject name changes.

## Publish MSIX (unsigned)

```powershell
dotnet publish src/ObsidianQuickNoteWidget/ObsidianQuickNoteWidget.csproj `
  -c Release -p:Platform=x64 `
  -p:GenerateAppxPackageOnBuild=true `
  -p:AppxPackageSigningEnabled=false `
  -p:AppxBundle=Always `
  -p:UapAppxPackageBuildMode=SideloadOnly
```

The resulting `.msix` / `.msixbundle` lands under
`src/ObsidianQuickNoteWidget/bin/x64/Release/net10.0-windows10.0.26100.0/AppPackages/…`.

`make pack` wraps the same command.

## Sign MSIX

```powershell
.\tools\Sign-DevMsix.ps1 <path-to-msix>
```

[`../../tools/Sign-DevMsix.ps1`](../../tools/Sign-DevMsix.ps1) reads
`password.txt` at runtime and invokes:

```
signtool sign /fd SHA256 /a /f <dev.pfx> /p <password-from-file> <msix>
```

The password is never echoed and is cleared from memory in the `finally`.
[`../../Makefile`](../../Makefile)'s `pack-signed` target is reserved for
*release* signing and **refuses** any `SIGNING_CERT` path containing
`dev-cert\` (F-01 defence-in-depth).

## Install locally

```powershell
Add-AppxPackage -Path <path-to-msixbundle> -ForceApplicationShutdown
```

After any manifest change that touches `<Definition>` elements or sizes, do a
full uninstall + reinstall — Widget Host caches definition metadata per-install
and `-Force*` leaves stale entries:

```powershell
Get-AppxPackage *ObsidianQuickNoteWidget* | Remove-AppxPackage
Add-AppxPackage <path>
```

## Kick Widget Host

Widget Host (`Widgets.exe`) + its service (`WidgetService.exe`) cache CLSID
lookups; after reinstall, bounce them:

```powershell
Get-Process Widgets,WidgetService -ErrorAction SilentlyContinue | Stop-Process -Force
```

In stubborn cases also kick `WebExperienceHost.exe`, `dasHost.exe`, and any
lingering `ObsidianQuickNoteWidget.exe`.

## Debugging

**Log file:**

- Unpackaged (running `dotnet run` against the widget directly):
  `%LocalAppData%\ObsidianQuickNoteWidget\log.txt`
- Packaged (Widget-Host-launched):
  `%LocalAppData%\Packages\<pfn>\LocalCache\Local\ObsidianQuickNoteWidget\log.txt`

The log rolls at 1 MB to `log.1`. Every line is run through
`FileLog.SanitizeForLogLine` ([`../../src/ObsidianQuickNoteWidget.Core/Logging/FileLog.cs`](../../src/ObsidianQuickNoteWidget.Core/Logging/FileLog.cs)) —
CR/LF/C0 → `\r` / `\n` / `\uXXXX` (F-03 closed).

**Tail it:**

```powershell
Get-Content $env:LocalAppData\ObsidianQuickNoteWidget\log.txt -Wait -Tail 40
```

**Reproduce locally:**

1. Tail the log (above).
2. Pin the widget from ⊞+W → *+ Add widgets* → Obsidian Quick Note.
3. Interact with the card; each `OnActionInvoked` logs `id=<guid> verb=<verb>`
   before dispatch in
   [`../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:115`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs).
4. If a card never renders: look for `CoRegisterClassObject failed 0x…` or a
   missing `CreateWidget` entry. If present but blank: look for the
   `PushUpdate failed` line with the inner exception.

There is also a proof-of-life file at `%UserProfile%\ObsidianWidget-proof.log`
written on every COM-server launch
([`Program.cs:20-28`](../../src/ObsidianQuickNoteWidget/Program.cs)) — useful
for confirming Widget Host actually spawned the server at all. F-05 flags this;
it's intentionally kept for now.

## See also

- [`architecture.md`](./architecture.md) — how the COM server, core, and tray fit together.
- [`adaptive-cards.md`](./adaptive-cards.md) — card / data binding contract.
- [`cli-surface.md`](./cli-surface.md) — every `obsidian` verb we use.
- [`testing.md`](./testing.md) — test layout and fakes.
- [`release.md`](./release.md) — the deploy pipeline.
- [`security.md`](./security.md) — threat model + F-series status.

Up: [`../README.md`](../README.md)
