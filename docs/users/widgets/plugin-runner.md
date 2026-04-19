# Plugin Runner widget ("Obsidian Actions")

This page is for users who want one-tap buttons on the Widget Board that trigger any Obsidian command — core or plugin-provided.

↑ Back to [docs index](../../README.md) · [User docs](../getting-started.md)

Shipped in **1.0.0.4**.

## What it does

Each tile is a button that runs an Obsidian command. Under the hood it runs `obsidian command id=<command-id>`, the same thing the built-in command palette does. After each tap you'll see a small ✓ (success) or ! (error) indicator on the tile.

## Grid sizes

| Size   | Tiles visible |
| ------ | ------------- |
| Small  | 2             |
| Medium | 4             |
| Large  | 6             |

If you add more actions than fit, a `+N more` hint appears at the bottom-right on the Large layout. Resize the widget to see them, or remove ones you don't use (see below).

## Adding an action

1. Tap the **⚙** (gear) icon in the widget's header. On the Large size it's labeled **⚙ Customize**.
2. The widget flips to the customize view.
3. Under **Add action**, fill in:
   - **Label** — what shows on the tile (up to 64 chars). e.g. `New tab`.
   - **Command id** — the Obsidian command to run (up to 256 chars). e.g. `workspace:new-tab`.
4. Tap **Add**.
5. Tap **Done** (top-right) to go back to the grid.

## Finding command IDs

Open any terminal (PowerShell works) **while Obsidian is running** and run:

```powershell
obsidian commands
```

That prints every command ID available — core Obsidian plus anything added by plugins you have installed. To narrow the list:

```powershell
obsidian commands filter=workspace
obsidian commands filter=daily
```

Useful starters:

| Label            | Command id                  |
| ---------------- | --------------------------- |
| New tab          | `workspace:new-tab`         |
| Command palette  | `command-palette:open`      |
| Open today's daily note | `daily-notes:open-today` |
| Toggle left panel | `app:toggle-left-sidebar`  |

Plugin commands look the same — e.g. `templater-obsidian:insert-templater` after installing Templater.

## Managing actions

From the customize view (**⚙**), each existing action has three buttons:

- **📌 / 📍 Pin / Unpin** — pinned actions sort to the top of the grid.
- **✕ Remove** — deletes the action. You'll get a confirmation screen before it's actually removed, because there's no undo.

## Security note

Every action runs with **full Obsidian privileges** — there's no sandbox. If you add a command, it can do anything Obsidian itself can do, including actions exposed by any plugin you have installed. Be thoughtful about what you pin, and double-check command IDs you paste in from the internet before adding them.

## Related

- [Quick Note widget](./quick-note.md)
- [Recent Notes widget](./recent-notes.md)
- [Troubleshooting](../troubleshooting.md)

↑ Back to [docs index](../../README.md)
