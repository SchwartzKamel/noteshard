# Quick Note widget

This page is for users who want to create notes in their Obsidian vault from the Windows 11 Widget Board.

↑ Back to [docs index](../../README.md) · [User docs](../getting-started.md)

## What each size offers

You can resize the widget from its three-dot menu. Each size exposes a different subset of the form.

### Small

A minimal, one-field card: a **Title** box and a green **Create** button. Use this when you want a fast capture and the defaults (vault root, blank template) are fine.

### Medium

Adds the fields you need for real note-taking:

- **Title** (required).
- **Folder** dropdown — every folder in your vault.
- **New folder** — optional text input that overrides the dropdown.
- **Body** — multi-line markdown.
- **Paste → body** button — pastes the clipboard into the body.
- **Create note** button.

### Large

Everything in Medium, plus an **Advanced** section you toggle with a button at the bottom:

- **Tags** — comma-separated list (e.g. `idea, research`).
- **Template** — Blank / Daily / Meeting / Book / Idea.
- **Toggles:**
  - **Date prefix** — prepends today's date to the filename (e.g. `2025-11-24 My note.md`).
  - **Open after** — opens the new note in Obsidian once created.
  - **Append daily** — also appends the body to today's daily note.

## How folder picking works

The folder dropdown lists every folder returned by `obsidian folders`. The **New folder** input sits below it and works as an override:

- **If New folder is empty**, the picker value is used.
- **If New folder has text**, that path wins — even if you also picked something from the dropdown.
- **Missing folders are created automatically.** Type `Inbox/2025/November` and the widget will create every missing segment via `obsidian create` when you save. You don't need to create the folder by hand first.

If you later hit "wrong vault selected", see [troubleshooting](../troubleshooting.md#wrong-vault-selected).

## Templates (Large size)

| Template | What it does                                                |
| -------- | ----------------------------------------------------------- |
| Blank    | No frontmatter, no body scaffolding — whatever you type.    |
| Daily    | Daily-note scaffolding (date header, sections).             |
| Meeting  | Attendees, agenda, action items sections.                   |
| Book     | Title/author frontmatter, notes sections.                   |
| Idea     | Short "idea stub" with tags.                                |

## Toggles (Large size)

- **Date prefix** — adds `YYYY-MM-DD ` to the front of the filename so notes sort chronologically.
- **Open after create** — once the note is written, it opens in Obsidian. If Obsidian isn't running, it's launched via the `obsidian://` URI scheme.
- **Append to daily** — in addition to creating the new note, the body is also appended to today's daily note via `obsidian daily:append`.

## Paste from clipboard

The **Paste → body** button copies your clipboard into the Body field. The [tray companion](../tray-companion.md) does the same thing automatically when you open it with `Ctrl+Alt+N`.

## Status messages

After you press **Create**:

- **Success** shows a confirmation in the widget's accent color (for example, *"Created: Inbox/2025-11-24 My note.md"*). Note that Obsidian's CLI silently renames collisions — if `My note.md` already exists you may get `My note 1.md` back.
- **Error** shows a message in red. The most common causes are:
  - Obsidian isn't running (the widget's CLI calls require a live Obsidian instance).
  - The title is empty (the form will refuse to submit).
  - The vault's folder can't be written (permissions).

See [troubleshooting](../troubleshooting.md) for fixes to the common ones.

## Related

- [Recent Notes widget](./recent-notes.md)
- [Plugin Runner widget](./plugin-runner.md)
- [Troubleshooting](../troubleshooting.md)

↑ Back to [docs index](../../README.md)
