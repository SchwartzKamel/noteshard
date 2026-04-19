# Tray companion

This page is for users who want to capture a note in Obsidian from anywhere in Windows — without opening the Widget Board.

↑ Back to [docs index](../README.md) · [User docs](./getting-started.md)

## What it is

`ObsidianQuickNoteTray` is a small companion app that lives in your notification area (system tray). It shares its note-creation pipeline with the Widget Board widget, so a note captured from the tray lands in your vault exactly the same way — same folder logic, same templates, same "append to daily" behavior.

Source lives under [`src/ObsidianQuickNoteTray/`](../../src/ObsidianQuickNoteTray/).

## The global hotkey: Ctrl+Alt+N

Press **Ctrl+Alt+N** from anywhere in Windows. A pop-up form appears with:

- A **Title** field (focused, ready to type).
- A **Body** field pre-filled with your clipboard contents, if they're text.
- The same folder, template, and toggle options as the Large widget size.

Press **Create** to write the note. The form closes on success.

If the hotkey collides with another app, the tray will show a balloon tip: *"Global hotkey Ctrl+Alt+N is unavailable (already in use). Use the tray icon instead."* Double-click the tray icon, or right-click → **New note**, to open the same form.

## Starting the tray

The tray app is a plain `.exe` — launch it however you like:

- **One-off**: double-click `ObsidianQuickNoteTray.exe` from wherever you installed it (typically next to the widget package's `InstallLocation`; run `Get-AppxPackage ObsidianQuickNoteWidget | Select-Object InstallLocation` to find it).

- **Every login**: add a shortcut to the Startup folder. Press `Win + R`, type:

  ```text
  shell:startup
  ```

  Press Enter, then drop a shortcut to `ObsidianQuickNoteTray.exe` into that folder.

- **Or via Task Scheduler** if you want it running before you sign in, elevated, or under specific conditions.

## Tray menu

Right-click the tray icon:

- **New note** — opens the same pop-up form the hotkey opens.
- **Exit** — shuts the tray down. (The global hotkey stops working.)

Double-click the icon to open the form without going through the menu.

## If the CLI isn't reachable

When the tray starts, it checks that `obsidian` is resolvable. If not, it shows a balloon tip: *"Obsidian CLI not found. Enable it in Obsidian → Settings → General → Command Line Interface, then restart Windows."* Fix the CLI (see [troubleshooting → CLI not found](./troubleshooting.md#cli-not-found)) and re-launch the tray.

## Logs

Tray actions log to the same file the widget uses:

```text
%LocalAppData%\ObsidianQuickNoteWidget\log.txt
```

Open it in any text editor. If the tray refuses to create a note but the widget works (or vice versa), this file is where to look first.

## Related

- [Quick Note widget](./widgets/quick-note.md) — same pipeline, different surface.
- [Troubleshooting](./troubleshooting.md)

↑ Back to [docs index](../README.md)
