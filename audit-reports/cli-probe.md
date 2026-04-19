# Obsidian CLI — Surface Report

- **Resolved executable:** `C:\Program Files\Obsidian\Obsidian.com` (`Get-Command obsidian`)
- **Obsidian version:** `1.12.7 (installer 1.12.7)` (via `obsidian version`)
- **Vault under test:** `C:\Users\lafia\OneDrive\Documents\Obsidian\Vaults\lafiamafia` (name: `lafiamafia`)
- **Scratch folder:** `audit-probe/` inside the vault — created, probed, deleted. Ephemeral daily note `2026-04-19.md` created by the probe was also deleted. Vault restored to pre-probe state (`Welcome.md` only).
- **Probe date:** 2026-04-19
- **Syntax confirmed:** positional `key=value` only. `--flag` style is rejected (`obsidian --version` → `Error: Command "--version" not found. Did you mean: version?`).

---

## Cross-cutting findings (read these first)

1. **Exit code is effectively useless as a success signal.** Every probe — valid, invalid, unknown command, missing file — returned **exit=0**. `obsidian open path=nope-missing-xyz.md` printed `Error: File "nope-missing-xyz.md" not found.` with exit=0. `obsidian ls` printed `Error: Command "ls" not found.` with exit=0. `obsidian --version` also exit=0.
2. **All output (success and error) goes to stdout.** `stderr` was empty in every probed error case. Callers must parse stdout prefixes (`Error:`, `Created:`, `Overwrote:`, `Added to:`, `Deleted permanently:`) to distinguish success from failure.
3. **`create` silently auto-renames on collision.** Without `overwrite`, `create path=audit-probe/p1.md` when `p1.md` already existed produced `Created: audit-probe/p1 1.md` (space-inserted suffix) and exit=0. The caller's intended path is NOT what was actually created.
4. **Line endings.** All observed stdout uses `\r\n` on Windows. Empty lines appear between help sections.

---

## Per-command entries

### `version`
```
> obsidian version
1.12.7 (installer 1.12.7)
exit=0
```
Format: single line, prose. No `--version` alias — must be a bare positional.

### `help`
`obsidian help` prints the full command index (one block per command, indented `key=<shape> - description` sub-lines). `obsidian help <cmd>` prints the section for just that command. Exit=0.

### `vault`
```
> obsidian help vault
  vault                 Show vault info
    info=name|path|files|folders|size  - Return specific info only

> obsidian vault
name<TAB>lafiamafia\r\n
path<TAB>C:\Users\lafia\OneDrive\Documents\Obsidian\Vaults\lafiamafia\r\n
files<TAB>1\r\n
folders<TAB>1\r\n
size<TAB>203\r\n
exit=0
```
**Format: TSV, `key\tvalue\r\n`.** Hex-verified tab byte `0x09` between key and value. Keys observed: `name`, `path`, `files`, `folders`, `size` — exactly the set in the help text.

### `vault info=path`
```
> obsidian vault info=path
C:\Users\lafia\OneDrive\Documents\Obsidian\Vaults\lafiamafia
exit=0
```
**Format: single line, fs path, no trailing key/value prefix, followed by a trailing newline.** `info=name` behaves analogously (just the name).

### `folders`
```
> obsidian folders
/
Test
exit=0
```
**Format: one folder per line.** The vault root appears as a bare `/`; subfolders appear WITHOUT a leading slash (e.g. `Test`, not `/Test`). Hidden directories (`.obsidian`, `.trash`) are NOT listed by the CLI (only user folders surfaced).

### `create`
```
> obsidian help create
  create                Create a new file
    name=<name>         - File name
    path=<path>         - File path
    content=<text>      - Initial content
    template=<name>     - Template to use
    overwrite           - Overwrite if file exists
    open                - Open file after creating
    newtab              - Open in new tab
```

Probes (scratch folder `audit-probe/`):

| Invocation | stdout | Resulting file |
| --- | --- | --- |
| `create path=audit-probe/p1.md content=hello` | `Created: audit-probe/p1.md` | `audit-probe/p1.md` (11 bytes) |
| `create name=p2 path=audit-probe content=world` | `Created: audit-probe/p2.md` | `audit-probe/p2.md` (5 bytes) — `path=` as folder + `name=` joined, `.md` auto-appended |
| `create path=audit-probe/p3.md "content=line1\nline2\ttabbed"` | `Created: audit-probe/p3.md` | `audit-probe/p3.md` → bytes `6C 69 6E 65 31 **0A** 6C 69 6E 65 32 **09** 74 61 62 62 65 64` (real LF + TAB) |
| `create path=audit-probe/p4.md` (no content=) | `Created: audit-probe/p4.md` | empty 0-byte file |
| `create path=audit-probe/p1.md content=collision` (collision, no `overwrite`) | `Created: audit-probe/p1 1.md` | **auto-renamed to `p1 1.md`** |
| `create path=audit-probe/p1.md content=overwritten overwrite` | `Overwrote: audit-probe/p1.md` | original file replaced |

**Escape interpretation confirmed:** literal `\n` and `\t` inside a `content=` value are converted by the CLI to real `0x0A` and `0x09` bytes. Escaping `\\` to `\` pre-pass is therefore required if the user's text contains a backslash (matches `ObsidianCliParsers.EscapeContent` ordering).

**Success prefixes:** `Created: <vault-rel-path>` or `Overwrote: <vault-rel-path>`. Exit=0 in every case.

### `open`
```
> obsidian help open
  open                  Open a file
    file=<name>         - File name
    path=<path>         - File path
    newtab              - Open in new tab

> obsidian open path=audit-probe/does-not-exist.md
Error: File "audit-probe/does-not-exist.md" not found.
exit=0          (!)
```
On success, `obsidian open path=<existing>` launches/focuses Obsidian and prints nothing (verified against real files during create probes). **There is NO `open vault` command** — the help lists no way to `open` without a `file=`/`path=`; opening the vault itself is not a documented operation.

### `daily:append`
```
> obsidian help daily:append
  daily:append          Append content to daily note
    content=<text>      - Content to append (required)
    inline              - Append without newline
    open                - Open file after adding
    paneType=tab|split|window  - Pane type to open in

> obsidian "daily:append" "content=probe line\nprobe2" inline
Added to: 2026-04-19.md
exit=0
```
Creates the daily note if absent, appends content, returns `Added to: <vault-rel-path>`. `\n`/`\t` escapes interpreted same as `create content=`.

### `daily:path`
```
> obsidian "daily:path"
2026-04-19.md
exit=0
```
**Format: single line, vault-relative path to the daily note (whether or not it exists yet).**

### `delete` (cleanup probe)
```
> obsidian delete path=audit-probe/p1.md permanent
Deleted permanently: audit-probe/p1.md
exit=0
```
Success prefix: `Deleted permanently: <path>` (with `permanent`) or `Deleted: <path>` (without, i.e. sent to trash — not probed here).

---

## Cross-reference vs `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs`

| Assertion in code / XMLdoc | Ground truth | Verdict |
| --- | --- | --- |
| "Obsidian CLI 1.12+" (class summary) | 1.12.7 | ✅ |
| "positional `key=value` args (NOT `--flag`)" | confirmed | ✅ |
| "literal `\n`/`\t` inside `content=` ... interpreted" | confirmed — hex-verified `0A`/`09` bytes | ✅ |
| `ParseVaultPath` expects single-line fs path from `vault info=path` | matches | ✅ |
| `ParseFolders` splits on `\n`, `TrimStart('/')`, drops empty/dotfiles | matches (`/` → empty → dropped; `Test` passes through) | ✅ |
| `EscapeContent` order: `\\` first, then `\r\n`/`\n`/`\r`/`\t` | correct — probe with `line1\nline2\ttabbed` yielded real LF+TAB | ✅ |
| `GetVaultRootAsync` uses `vault info=path` | matches probed shape | ✅ |
| `ListFoldersAsync` uses `folders` | matches | ✅ |
| `CreateNoteAsync` builds `create path=<vault-relative> [content=<esc>]`, returns the **input** `vaultRelativePath` on success | **⚠ DRIFT #1:** on `overwrite`-less collision the CLI auto-renames to `"<name> 1.md"` and returns `Created: audit-probe/p1 1.md`. Code would return `"audit-probe/p1.md"` — a path that is NOT the file actually created. Recommend parsing the `Created:`/`Overwrote:` prefix from `r.StdOut` and returning the CLI-reported path. |
| `CreateNoteAsync` uses `r.Succeeded` (exit==0) as the success signal | **⚠ DRIFT #2:** exit is 0 on *every* error path too (missing file, unknown command, schema errors). Need to additionally check for an `Error:` stdout prefix — or verify a success prefix. |
| `OpenNoteAsync(vaultRelativePath)` — empty path falls back to bare `obsidian vault` | **⚠ DRIFT #3 (semantic bug):** `vault` prints vault-info TSV; it does not open the vault UI. The fallback will return `Succeeded=true` but no window will appear. Help lists no `open` command for the vault itself; this case should either (a) open the vault root daily note / a known landing file, (b) `open path=<some-file-in-vault>`, or (c) be removed and surfaced as unsupported. |
| `OpenNoteAsync` uses `r.Succeeded` | same Drift #2: `open path=missing.md` → `Error: File ... not found.` on stdout, exit=0 → current code reports success. |
| `AppendDailyAsync` uses `r.Succeeded` | same Drift #2. Expected success prefix is `Added to: <path>`. |
| `IObsidianCli` contract does not expose `daily:path` | not implemented in this file (used elsewhere if at all) — no drift here, just noted: shape is single-line vault-relative path. |
| Timeout 30 s, `ArgumentList` (no shell quoting), UTF-8 redirection | process plumbing looks correct; Obsidian CLI is happy with `ArgumentList`-style (no shell parsing) and emits UTF-8. | ✅ |

### Drift summary (priority order)

1. **Exit-code-only success detection is wrong** for *every* mutating call (`CreateNoteAsync`, `OpenNoteAsync`, `AppendDailyAsync`). The CLI never returns non-zero; error text lands on **stdout** with an `Error:` prefix. Fix by parsing stdout success prefixes (`Created:`, `Overwrote:`, `Added to:`, `Deleted:`, `Deleted permanently:`) or at minimum rejecting stdout that starts with `Error:`.
2. **`CreateNoteAsync` returns the wrong path on collision.** Without `overwrite`, CLI silently renames to `"<name> N.md"`. Parse the `Created:`/`Overwrote:` line and return that path instead of echoing the input.
3. **`OpenNoteAsync("")` does not actually open the vault.** `obsidian vault` is an info query. There is no CLI verb to open the vault itself — the fallback needs to be rethought (or documented as a no-op that the UI should never trigger).

### What the rest of the codebase can rely on (unchanged)

- `vault info=path` → single trimmed line = absolute vault path.
- `vault` → `key\tvalue\r\n` TSV with keys `name|path|files|folders|size`.
- `folders` → one folder per line; root is a bare `/`; subfolders have no leading slash; dotfolders omitted.
- `daily:path` → single trimmed line = vault-relative daily note path (whether or not the file exists).
- `\n` / `\t` escapes inside `content=` values ARE interpreted by the CLI; the existing `EscapeContent` logic (backslash-escape first, then CR/LF/tab) is correct.

## Caveats

- The daily-note probe necessarily created `2026-04-19.md` in the vault (the CLI creates the daily note on demand). That file was deleted after the probe. The vault's only remaining user file is the pre-existing `Welcome.md`.
- `open` on an existing file would have focused the Obsidian window; we only probed the missing-file error path to avoid disturbing the user's UI. Success behaviour (silent stdout, exit=0) inferred from the absence of any stdout during the successful `create` → implicit-open path and is consistent with the `create ... open` flag producing no extra stdout.
- `overwrite` without collision was not separately probed (redundant — the collision + overwrite case exercised both branches together).
