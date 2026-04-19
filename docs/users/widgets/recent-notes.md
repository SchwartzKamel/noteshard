# Recent Notes widget

This page is for users who want a one-click jump list of their recently opened Obsidian notes on the Widget Board.

↑ Back to [docs index](../../README.md) · [User docs](../getting-started.md)

## What it shows

A list of notes you've recently opened in your active vault. Each row shows the note title and, if applicable, its folder. Tap a row to open the note.

The list comes from `obsidian recents`, then intersected with `obsidian files` so that deleted-but-still-remembered entries ("ghost" recents) get dropped before they're shown.

## Refreshing

The widget refreshes itself automatically every 30 seconds or so — you don't need to do anything. Open a note in Obsidian, wait a moment, and it'll appear at the top of the list next time the widget ticks.

## Opening a note

Click any row. The widget builds an `obsidian://open?vault=…&file=…` URI and hands it to Windows, which launches Obsidian if it isn't already running. This is a fix that shipped in **1.0.0.7** — older versions needed Obsidian to already be open.

## Buttons

- **Create note** — jumps to the [Quick Note](./quick-note.md) flow so you can capture a new note without resizing the widget.
- **Open vault** — opens your active vault in Obsidian, also via the `obsidian://` URI scheme. If Obsidian isn't running, it's launched.

If nothing happens when you press **Open vault**, confirm you're on **1.0.0.7 or newer** — see [troubleshooting](../troubleshooting.md#open-vault-button-does-nothing).

## Which vault is "active"?

The widget reads `%APPDATA%\obsidian\obsidian.json` and prefers, in order: the vault marked `"open": true`; then the one with the newest `ts`; then the first entry. You can override this with the `OBSIDIAN_VAULT` environment variable (leaf folder name). See [troubleshooting → wrong vault](../troubleshooting.md#wrong-vault-selected).

## If you see no notes at all

- Make sure Obsidian is running at least once so that `obsidian recents` has something to return.
- Check that the CLI is reachable — see [troubleshooting → CLI not found](../troubleshooting.md#cli-not-found).

If entries show up but point at notes that no longer exist, you probably need version **1.0.0.6 or newer** — see [troubleshooting](../troubleshooting.md#recent-notes-shows-ghost-files).

## Related

- [Quick Note widget](./quick-note.md)
- [Plugin Runner widget](./plugin-runner.md)
- [Troubleshooting](../troubleshooting.md)

↑ Back to [docs index](../../README.md)
