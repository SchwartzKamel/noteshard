# Troubleshooting

This page is for users who hit a problem with Obsidian Quick Note Widget and want a concrete fix.

↑ Back to [docs index](../README.md) · [User docs](./getting-started.md)

Each section below is a symptom → fix. If yours isn't listed, check the [log](#where-are-my-logs) first.

---

## Widget doesn't appear after install

The Widget Board caches its manifest and sometimes misses a new package.

1. Open Task Manager.
2. End both `Widgets.exe` and `WidgetService.exe` (Windows will restart them on demand).
3. Re-open the Widget Board (`Win + W`) and try **+ Add widgets** again.

If that doesn't work, see [can't pin widget](#cant-pin-widget) below.

---

## Can't pin widget

Re-register the installed package. Open PowerShell **as Administrator**:

```powershell
$pkg = Get-AppxPackage ObsidianQuickNoteWidget
Add-AppxPackage -DisableDevelopmentMode -Register "$($pkg.InstallLocation)\AppxManifest.xml"
```

Then restart the Widgets processes (see above) and try pinning again.

---

## Open vault button does nothing

The **Open vault** button in the Recent Notes widget requires the URI-scheme launcher that shipped in **1.0.0.7**. Check your installed version:

```powershell
Get-AppxPackage ObsidianQuickNoteWidget | Select-Object Name, Version
```

If `Version` is older than `1.0.0.7`, reinstall with the newer MSIX.

---

## Recent Notes shows ghost files

If clicking a row shows "file not found" or the list contains notes you've already deleted, you need the ghost-filter fix that shipped in **1.0.0.6** — it intersects Obsidian's recents list with the live file list before displaying. Upgrade:

```powershell
Get-AppxPackage ObsidianQuickNoteWidget | Select-Object Name, Version
```

If older than `1.0.0.6`, install a newer MSIX.

---

## CLI not found

The widget can't locate `obsidian`. Fix in this order:

1. **Install or update Obsidian to 1.12 or newer.**
2. **Enable the CLI** — Obsidian → **Settings** → **General** → **Command Line Interface** → turn it on.
3. **Restart Windows** so the new `PATH` / install location is visible to the Widget Service.

The widget searches for the CLI in this order:

1. `%ProgramFiles%\Obsidian\Obsidian.(com|exe)`
2. `%LocalAppData%\Programs\Obsidian\Obsidian.(com|exe)`
3. The registered `obsidian://` protocol handler in the registry.
4. Any `obsidian.com` or `obsidian.exe` on `PATH`.

If yours lives somewhere unusual, set an override:

```powershell
[Environment]::SetEnvironmentVariable(
  "OBSIDIAN_CLI",
  "D:\Apps\Obsidian\Obsidian.exe",
  "User")
```

Sign out and back in for the user-scope env var to take effect.

---

## Wrong vault selected

The widget uses your `%APPDATA%\obsidian\obsidian.json` to decide which vault is active. Two ways to change what it picks:

### Option A — set a preference in Obsidian

Make sure the vault you want has `"open": true` in `%APPDATA%\obsidian\obsidian.json`. Obsidian sets this when you open a vault normally; if it's missing, open the vault once through Obsidian's vault picker.

### Option B — force it with an env var

Set `OBSIDIAN_VAULT` to the **leaf folder name** of the vault (not the full path):

```powershell
[Environment]::SetEnvironmentVariable("OBSIDIAN_VAULT", "MyVault", "User")
```

Sign out and back in.

---

## Where are my logs?

```text
%LocalAppData%\ObsidianQuickNoteWidget\log.txt
```

Open from PowerShell:

```powershell
notepad "$env:LOCALAPPDATA\ObsidianQuickNoteWidget\log.txt"
```

Include recent lines when asking for help.

---

## Related

- [Getting started](./getting-started.md)
- [Quick Note widget](./widgets/quick-note.md)
- [Recent Notes widget](./widgets/recent-notes.md)
- [Plugin Runner widget](./widgets/plugin-runner.md)
- [Tray companion](./tray-companion.md)

↑ Back to [docs index](../README.md)
