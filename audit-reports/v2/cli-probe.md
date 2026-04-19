# Obsidian CLI — Surface Report v2 (verification sweep)

- **Resolved executable:** `C:\Program Files\Obsidian\Obsidian.com`
- **Obsidian version:** `1.12.7 (installer 1.12.7)` — unchanged from v1
- **Vault under test:** `C:\Users\lafia\OneDrive\Documents\Obsidian\Vaults\lafiamafia`
- **Scratch folder:** `audit-probe-v2/` — created, probed, deleted
- **Ephemeral daily note:** `2026-04-19.md` — created by `daily:append`, deleted post-probe
- **Probe date:** 2026-04-19
- **Goal:** verify the three v1 drifts are resolved in
  `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs` +
  `ObsidianCliParsers.cs`, and re-check that CLI stdout shapes remain
  stable for this version.

---

## Live probe log (real stdout, verbatim)

```text
> obsidian version
1.12.7 (installer 1.12.7)                                         exit=0

> obsidian create path=audit-probe-v2/p1.md content=hello
Created: audit-probe-v2/p1.md                                     exit=0

> obsidian create path=audit-probe-v2/p1.md content=second        # collision, no overwrite
Created: audit-probe-v2/p1 1.md                                   exit=0

> obsidian create path=audit-probe-v2/p1.md content=third overwrite
Overwrote: audit-probe-v2/p1.md                                   exit=0

> obsidian create 'path=audit-probe-v2/bad<>name.md' content=x
Error: File name cannot contain any of the following characters: * " \ / < > : | ?
                                                                  exit=0

> obsidian open path=audit-probe-v2/does-not-exist.md
Error: File "audit-probe-v2/does-not-exist.md" not found.         exit=0

> obsidian daily:append content=probe-v2 line inline
Added to: 2026-04-19.md                                           exit=0

> obsidian daily:path
2026-04-19.md                                                     exit=0
```

Filesystem confirmation in `audit-probe-v2/` after the three `create`
probes: `p1.md` (6 bytes, content=`third`) + `p1 1.md` (6 bytes,
content=`second`). Both deleted at cleanup; vault restored to pre-probe
state (`Welcome.md` + `Test/` only).

---

## Verification of the three v1 drifts

### Drift #1 — `CreateNoteAsync` returned the *input* path on collision (wrong)

- **v1 behaviour:** code returned `vaultRelativePath` unconditionally,
  so the caller thought the note was at `p1.md` when the CLI silently
  wrote `p1 1.md`.
- **Current code** (`ObsidianCli.CreateNoteAsync`, lines 154–164):
  parses `Created:`/`Overwrote:` from stdout via
  `ObsidianCliParsers.TryParseCreated` and returns that path. Returns
  `null` if no success prefix is found.
- **Live probe confirms CLI still emits the expected shapes:**
  - Fresh write → `Created: audit-probe-v2/p1.md`
  - Collision (no `overwrite`) → `Created: audit-probe-v2/p1 1.md` (note the
    literal space before `1`, suffix grows on further collisions)
  - `overwrite` flag → `Overwrote: audit-probe-v2/p1.md`
- **`TryParseCreated` contract check** against the live output: handles
  both prefixes (`Created:` and `Overwrote:`), trims the path, and
  tolerates the CRLF line endings Windows emits. ✅
- **Verdict: RESOLVED.** The CLI's collision behaviour is unchanged and
  the parser + caller now propagate the authoritative CLI-reported
  path. No regression.

### Drift #2 — Exit code is not a success signal (every error returns exit=0)

- **v1 behaviour:** `CreateNoteAsync` / `OpenNoteAsync` /
  `AppendDailyAsync` all used `r.Succeeded` (exit==0) as the green
  light; every CLI error therefore silently read as success.
- **Current code:** each of the three methods now *also* calls
  `ObsidianCliParsers.HasCliError(r.StdOut)` and treats an
  `Error:`-prefixed line as failure (returning `null` / `false`
  respectively). Success-path methods additionally require the expected
  positive prefix via `TryParseCreated`.
- **Live probe confirms the premise still holds in 1.12.7:** every
  probed error (invalid characters, missing open target) prints an
  `Error:`-prefixed line **on stdout** with **exit=0**. stderr was
  empty in every case (unchanged from v1).
- **Prefix check against the live output:**
  - `Error: File name cannot contain any of the following characters: …`
    → matches `HasCliError` (case-sensitive `StartsWith("Error:")`). ✅
  - `Error: File "…" not found.` → matches. ✅
- **Verdict: RESOLVED.** All three mutating paths now gate on stdout,
  not exit code. `AppendDailyAsync` success prefix `Added to:` still
  matches the live CLI; note that the current implementation only
  *rejects* `Error:` — it does not require the `Added to:` prefix. See
  N-01 below.

### Drift #3 — `OpenNoteAsync("")` fell through to `obsidian vault` (bogus)

- **v1 behaviour:** empty path branched into `obsidian vault`, an info
  TSV query that returns `Succeeded=true` without opening any window.
- **Current code** (`ObsidianCli.OpenNoteAsync`, lines 167–177): empty
  / whitespace path short-circuits to a logged `_log.Warn(...)` and
  `return false`. No CLI invocation is made. ✅
- **Live CLI check:** I re-confirmed there is still no `obsidian` verb
  that opens "the vault itself". `obsidian help` surfaces `open` only
  with `file=` / `path=` args. The v1 workaround had no basis in the
  actual surface.
- **Verdict: RESOLVED.** Empty-path calls are now an explicit no-op.

---

## Cross-check: shapes the rest of the codebase depends on

All re-verified against live 1.12.7 stdout this run:

| Shape | v1 assertion | v2 observation | Verdict |
| --- | --- | --- | --- |
| `create` success line | `Created: <path>` / `Overwrote: <path>` | identical | ✅ |
| `create` collision auto-rename | `<name>.md` → `<name> 1.md` (space) | identical | ✅ |
| `create` error line | starts with `Error:` on stdout, exit=0 | identical (new variant captured: invalid-char message) | ✅ |
| `open` missing file | `Error: File "<path>" not found.` on stdout, exit=0 | identical | ✅ |
| `daily:append` success line | `Added to: <path>` | identical | ✅ |
| `daily:path` | single line, vault-relative | identical | ✅ |
| `version` | `1.12.7 (installer 1.12.7)` | identical — Obsidian has NOT updated since v1 | ✅ |

No new stdout-format drift detected. The CLI contract this widget
depends on is stable between the v1 probe and today.

---

## New observations / minor notes

- **N-01 (minor, non-blocking): `AppendDailyAsync` does not positively
  verify success.** Unlike `CreateNoteAsync` (which requires
  `TryParseCreated` to match), `AppendDailyAsync`
  (`ObsidianCli.cs:190–201`) only rejects `Error:` lines and otherwise
  returns `true`. If the CLI ever emits unexpected non-error stdout
  (e.g. a future warning line), this would read as success. Consider
  gating on `ObsidianCliParsers.TryParseAppendedDaily` — which already
  exists and parses `Added to:` — for symmetry with `CreateNoteAsync`.
  Not a current bug against 1.12.7; flagged only for robustness.
- **N-02 (documentary): `Error:` prefixes seen in the wild.** This
  probe added a second concrete error shape to the catalogue:
  `Error: File name cannot contain any of the following characters:
  * " \ / < > : | ?` (emitted by `create` when the path contains any
  of those characters). `HasCliError`'s prefix-only strategy handles
  it correctly without needing per-message logic.
- **N-03 (no-op): Obsidian has not been updated.** Version unchanged
  from the v1 report (`1.12.7 (installer 1.12.7)`), so no
  stdout-format regressions were possible. If a future Obsidian
  update bumps this string, the full v1 probe matrix should be re-run
  before trusting these parsers.

---

## Caveats

- Only the stdout contract was re-probed; the PATH-resolution and
  Authenticode-signature concerns flagged in
  `audit-reports/security-auditor.md` (F-02) are out of scope for this
  agent and untouched by the v2 sweep.
- Successful `open path=<existing>` would have focused the Obsidian
  window, so only the error path was probed (consistent with v1
  methodology).
- `overwrite` was probed positively this run (v1 only probed it via
  the collision path). Stdout shape `Overwrote: <path>` confirmed
  directly.
