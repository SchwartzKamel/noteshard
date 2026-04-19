# Getting started

This page is for users who want to install Obsidian Quick Note Widget on Windows 11 and pin it to the Widget Board.

↑ Back to [docs index](../README.md)

## What you need

- **Windows 11, version 22H2 or newer.** The Widget Board needs a recent build — the package manifest sets a minimum of `10.0.22621.0`.
- **Obsidian 1.12 or newer**, with the built-in command-line interface enabled (Obsidian → **Settings** → **General** → **Command Line Interface**). The widget talks to Obsidian through the `obsidian` CLI and the `obsidian://` URI scheme.
- **At least one vault already created** in Obsidian. The widget reads your vault list from `%APPDATA%\obsidian\obsidian.json` and uses whichever vault has `"open": true`, or the most recently opened one.

## Install

### From a local MSIX (current dev path)

Until the package ships to winget, install the signed MSIX produced by the build:

```powershell
Add-AppxPackage -Path .\ObsidianQuickNoteWidget_1.0.0.7_x64.msix
```

If the MSIX is signed by a certificate your machine doesn't already trust, you'll need to install that certificate into `Trusted People` first. For the dev-cert bootstrap flow, see [contributing/development.md](../contributing/development.md).

### From winget (future)

Once the package is published, installation will be:

```powershell
winget install ObsidianMD.ObsidianQuickNoteWidget
```

(The `winget/` folder in the repo holds the manifest that will be submitted — this path is not live yet.)

## First run

1. **Open the Widget Board.** Press `Win + W`, or click the weather/news tile on the taskbar.
2. **Pin the widget.** Click **+ Add widgets**, find **Obsidian Quick Note**, and pin it. It registers three widget kinds:
   - **Quick Note** — create a note.
   - **Recent Notes** — jump back to a recent file.
   - **Obsidian Actions** (Plugin Runner) — one-tap command buttons.
3. **Make sure Obsidian is reachable.** The widget needs the `obsidian` CLI to be resolvable. It looks, in order:
   1. The `OBSIDIAN_CLI` environment variable, if set.
   2. `%ProgramFiles%\Obsidian\Obsidian.(com|exe)`.
   3. `%LocalAppData%\Programs\Obsidian\Obsidian.(com|exe)`.
   4. The registered `obsidian://` protocol handler in the registry.
   5. A `.com` or `.exe` named `obsidian` on your `PATH`.

   If none of those work, see [troubleshooting → "CLI not found"](./troubleshooting.md#cli-not-found).

4. **Confirm it registered.** Open PowerShell and run:

   ```powershell
   Get-AppxPackage ObsidianQuickNoteWidget
   ```

   You should see `Version : 1.0.0.7` (or newer) and a non-empty `InstallLocation`.

## If the Widget Board doesn't see the widget after install

Windows occasionally keeps stale widget manifests cached. Two fixes, in order:

1. **Restart the Widgets processes.** Open Task Manager, end `Widgets.exe` and `WidgetService.exe` (both are auto-restarted by Windows), then reopen the Widget Board.

2. **Re-register the package manually.** Open PowerShell **as Administrator** and run:

   ```powershell
   Add-AppxPackage -DisableDevelopmentMode -Register `
     "$env:ProgramFiles\WindowsApps\ObsidianQuickNoteWidget_1.0.0.7_x64__*\AppxManifest.xml"
   ```

   Adjust the path to match whatever `Get-AppxPackage ObsidianQuickNoteWidget` reports as `InstallLocation`.

## Next steps

- [Quick Note widget](./widgets/quick-note.md) — the main note-creation form.
- [Recent Notes widget](./widgets/recent-notes.md) — jump back to recent files.
- [Plugin Runner widget](./widgets/plugin-runner.md) — one-tap command buttons.
- [Tray companion](./tray-companion.md) — Ctrl+Alt+N from anywhere.
- [Troubleshooting](./troubleshooting.md) — common problems with copy-paste fixes.

↑ Back to [docs index](../README.md)
