# Obsidian CLI surface

> This page is for contributors editing `IObsidianCli` / `ObsidianCli` / `ObsidianCliParsers` or debugging a CLI-shaped regression. Every verb here has been verified against Obsidian 1.12+ with the CLI registered.

Up: [`../README.md`](../README.md) (docs index)

## Shape

`obsidian <command> [key=value ...]`. Arguments are **positional
`key=value` tokens**, not `--flags`. Values containing spaces must be quoted
(`name="My Note"`). Inside `content=` values, literal `\n` and `\t` are
decoded by the CLI; we escape accordingly in
[`ObsidianCliParsers.EscapeContent`](../../src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCliParsers.cs).

All process invocations go through
[`ObsidianCli.RunAsync`](../../src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs) using
`ProcessStartInfo.ArgumentList` (never the `Arguments` string), so args are
delivered to `CreateProcess` as separate argv elements — no shell interposition.

## Verbs this repo uses

### `obsidian vault info=path`

Prints a **single line**: the absolute filesystem path of the active vault.

Parsed by
[`ObsidianCliParsers.ParseVaultPath`](../../src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCliParsers.cs) — first non-empty trimmed line.
Used as the base for `NoteCreationService`'s duplicate-filename resolution and
as a sanity probe during CLI discovery.

### `obsidian folders`

Prints **one folder per line**, forward-slash separated, vault-root relative.
The vault root itself is a bare `/` line (collapses to empty after
`TrimStart('/')`).

Parsed by `ObsidianCliParsers.ParseFolders`:

- `Trim()` then `TrimStart('/')`
- Drop empty lines and dotfiles (any segment starting with `.` — `.obsidian`,
  `.trash`, `.git`)
- Dedupe case-insensitive, sort ordinal case-insensitive

Ghost caveat: `folders` may include folders that have been deleted on disk
for **~1–2 seconds** after deletion (Obsidian's in-memory vault index lags
the filesystem). Callers cache and refresh every 2 minutes — staleness is not
a correctness problem.

### `obsidian create name=… path=… content=… [template=…] [overwrite] [open] [newtab]`

Success stdout is one of:

- `Created: <vault-relative-path>` — new note.
- `Overwrote: <vault-relative-path>` — `overwrite` flag + existing path.

Failure stdout begins with `Error:`. **Exit code is 0 on both outcomes.**

Parsed by `ObsidianCliParsers.TryParseCreated`. The path segment is
authoritative: on a collision **without** `overwrite`, Obsidian silently
renames (`Plan.md` → `Plan 1.md`) and reports the real name — we persist that,
not the requested one (1.0.0.1 fix).

### `obsidian open path=…`

Succeeds **only when Obsidian is already running**. When Obsidian is closed
the verb is a no-op with no error surface — `Error:` is never emitted, `open`
just doesn't open anything. This is why the widget has a separate URI-scheme
launcher ([`ObsidianLauncher`](../../src/ObsidianQuickNoteWidget.Core/Cli/ObsidianLauncher.cs))
for `openVault` / `openRecent` — the URI handler launches Obsidian on demand.
See [`architecture.md`](./architecture.md#cli-and-uri-launcher-split).

### `obsidian daily:append content=… [inline] [open] [paneType=tab|split|window]`

Success stdout: `Added to: <vault-relative-path>`.
Parsed by `ObsidianCliParsers.TryParseAppendedDaily`.

### `obsidian recents`

Prints **up to 10 recently-opened paths** (files and folders mixed,
newest first). Backing store is Obsidian's "Recent files" plugin — may
include **ghost entries** for files deleted on disk (Obsidian doesn't prune
the list on filesystem deletion).

Parsed by `ObsidianCliParsers.ParseRecents(stdout, max)`:

- Trim, drop empty lines.
- Keep only entries ending in `.md` (case-insensitive) — folder entries are
  dropped.
- Dedupe case-insensitive, preserve order, cap at `max` (default 16).

Ghost-entry handling: the provider intersects recents with `obsidian files`
and caps at 16 — see `ObsidianWidgetProvider.IntersectRecentsWithFiles`
([`../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:500`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs)).
If `files` returns empty (CLI hiccup), intersection is skipped and recents
are returned as-is with a warning — "possibly stale" beats "blank widget"
(1.0.0.6).

### `obsidian files`

Prints **every live `.md` file** in the vault, one per line, vault-root
relative. Parsed by `ObsidianCliParsers.ParseFiles` — same shape as
`ParseRecents` but without the cap. Used solely to build the live-files set
that `recents` is intersected against.

### `obsidian command id=<id>`

Executes any Obsidian command by its internal id (e.g.
`daily-notes:open-today`, `editor:toggle-bold`). Success stdout:
`Executed: <id>`. Failure stdout: `Error: …`. Exit=0 on both.

Used by the Plugin Runner's `runAction` verb via
[`ObsidianCommandInvoker`](../../src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCommandInvoker.cs),
which maps the two shapes to a `{Success, ErrorMessage}` result.

### `obsidian commands filter=<prefix>`

Prints **one command id per line** matching `<prefix>`. Used today only for
manual exploration / paste-into-runner — there is no UI affordance yet.

## Convention: exit=0 on every error; stdout `Error:` is the failure signal

The Obsidian CLI reports **all** errors on stdout with **exit=0**, across
every verb this project uses. The authoritative failure check is
[`ObsidianCliParsers.HasCliError`](../../src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCliParsers.cs):

```csharp
public static bool HasCliError(string? stdout)
{
    if (string.IsNullOrEmpty(stdout)) return false;
    foreach (var raw in stdout.Split('\n'))
    {
        var line = raw.Trim();
        if (line.Length == 0) continue;
        if (line.StartsWith("Error:", StringComparison.Ordinal)) return true;
    }
    return false;
}
```

Exit-code based success detection was the pre-1.0.0.1 behavior; the
`cli-probe` audit traced several silent-success cases to it. Every new
mutating-verb integration must route its outcome parsing through
`ObsidianCliParsers` (or at minimum call `HasCliError` before treating
stdout as success output).

## Verbs explicitly not used

Mentioned here so a future contributor doesn't reinvent them:

- `obsidian vault` (no `info=path`) — TSV of vault metadata; more than we
  need and easier to misparse.
- `obsidian delete file=|path=` — destructive, no undo; not surfaced in the
  widget deliberately.
- `obsidian daily:path` — vault-relative path to today's daily note; we use
  `daily:append` directly.
- `obsidian vaults [verbose]` — list all configured vaults; the launcher
  reads `%APPDATA%\obsidian\obsidian.json` instead (same data, deterministic
  parse).
- `obsidian ls` — **does not exist**. Older docs / code referencing it are
  stale; use `obsidian folders` or `obsidian files`.

## See also

- [`architecture.md`](./architecture.md) — CLI vs URI-launcher split, and the
  data-flow for a createNote click.
- [`adaptive-cards.md`](./adaptive-cards.md) — which card verbs drive which
  CLI calls.
- [`security.md`](./security.md) — F-02 (PATH hardening), F-12
  (`OBSIDIAN_CLI` override), F-17 (`ArgumentList` usage).

Up: [`../README.md`](../README.md)
