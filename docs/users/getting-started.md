# Getting started

This page is for users who want to install Obsidian Quick Note Widget on Windows 11 and pin it to the Widget Board.

↑ Back to [docs index](../README.md)

## What you need

- **Windows 11, version 22H2 or newer.** The Widget Board needs a recent build — the package manifest sets a minimum of `10.0.22621.0`.
- **Obsidian 1.12 or newer**, with the built-in command-line interface enabled (Obsidian → **Settings** → **General** → **Command Line Interface**). The widget talks to Obsidian through the `obsidian` CLI and the `obsidian://` URI scheme.
- **At least one vault already created** in Obsidian. The widget reads your vault list from `%APPDATA%\obsidian\obsidian.json` and uses whichever vault has `"open": true`, or the most recently opened one.

## Install

Install from the project's GitHub Releases page. The package is signed, but because the signing certificate isn't from a public authority, you need to tell Windows to trust it once before installing.

1. **Download two files** from the [latest release](https://github.com/SchwartzKamel/noteshard/releases/latest):
   - `noteshard-signing.cer` — the signing certificate's public half.
   - `ObsidianQuickNoteWidget_<version>_x64.msix` — the app itself.

2. **Trust the certificate (one time).** Open PowerShell **as Administrator**, change into the folder where you saved the files, and run:

   ```powershell
   Import-Certificate -FilePath .\noteshard-signing.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

   You only need to do this once per machine. Future updates signed with the same certificate install without repeating this step.

3. **Install the package.** In the same folder, run:

   ```powershell
   Add-AppxPackage -Path .\ObsidianQuickNoteWidget_<version>_x64.msix
   ```

   Replace `<version>` with the actual version number in the filename you downloaded (for example, `1.0.0.10`).

> A winget manifest is tracked in `winget/` for future publication; follow releases for now.

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

   You should see a `Version` matching the `.msix` you installed (check the [latest release](https://github.com/SchwartzKamel/noteshard/releases/latest) if you're not sure what to expect) and a non-empty `InstallLocation`.

## If the Widget Board doesn't see the widget after install

Windows occasionally keeps stale widget manifests cached. Two fixes, in order:

1. **Restart the Widgets processes.** Open Task Manager, end `Widgets.exe` and `WidgetService.exe` (both are auto-restarted by Windows), then reopen the Widget Board.

2. **Re-register the package manually.** Open PowerShell **as Administrator** and run:

   ```powershell
   Add-AppxPackage -DisableDevelopmentMode -Register `
     "$env:ProgramFiles\WindowsApps\ObsidianQuickNoteWidget_<version>_x64__*\AppxManifest.xml"
   ```

   Replace `<version>` with your installed version. The easiest way to get the exact path is to run `Get-AppxPackage ObsidianQuickNoteWidget` first and use whatever it reports as `InstallLocation` (append `\AppxManifest.xml`).

## Next steps

- [Quick Note widget](./widgets/quick-note.md) — the main note-creation form.
- [Recent Notes widget](./widgets/recent-notes.md) — jump back to recent files.
- [Plugin Runner widget](./widgets/plugin-runner.md) — one-tap command buttons.
- [Tray companion](./tray-companion.md) — optional, for keyboard-first capture.
- [Troubleshooting](./troubleshooting.md) — common problems with copy-paste fixes.

↑ Back to [docs index](../README.md)
