# Security Auditor v3 — ObsidianQuickNoteWidget

**Archetype:** `security-auditor` (adversarial, code-level). **Mode:** READ-ONLY.
**Target:** `C:\Users\lafia\csharp\obsidian_widget`
**Predecessors:** `audit-reports/security-auditor.md` (v1, F-01..F-11),
`audit-reports/v2/security-auditor.md` (v2, F-12..F-15; confirmed F-01/F-02/F-03 closed).
**Scope (delta):** (a) re-verify F-01..F-08 (+F-12..F-15) still hold; (b) new
attack-surface review for the **`folderNew` card input** — adaptive-card
`Input.Text` whose value, when non-empty, becomes the vault-relative folder
passed through `FolderPathValidator` → `NoteCreationService` →
`DuplicateFilenameResolver.ResolveUnique` → `ObsidianCli.CreateNoteAsync` →
`obsidian create path=<…>` via `ProcessStartInfo.ArgumentList`.
**Tools run:** manual taint analysis + ripgrep. No new scanners installed.

---

## Executive summary

| Severity | Count (v3) | Δ vs v2 |
|----------|-----------:|--------:|
| CRITICAL | 0 | — |
| HIGH     | 0 | — |
| MEDIUM   | 1 | — (F-04 still the lone MED) |
| LOW      | 6 | +1 (F-16 new — control chars in `folderNew`) |
| INFO     | 6 | +2 (F-17, F-18 new — reparse-point TOCTOU + CLI-as-sink confirmation) |

**Top 3 risks (v3)**

1. **MED — F-04 (still open, promoted in severity-of-interest).** Leading-dot
   segments (`.obsidian/`, `.git/`, `.trash/`) still flow through
   `FolderPathValidator`. With the new `folderNew` free-text input, the
   attacker no longer needs to pre-populate a dropdown — any user tricked into
   pasting `.obsidian/workspace.json`-style text into the "new folder" box can
   have the note land in Obsidian's own config tree. Fix is still the single
   `seg.StartsWith('.')` guard recommended in v1.
2. **LOW — F-16 (new).** `FolderPathValidator.IllegalSegmentChars` lists only
   `: * ? " < > |`. It does **not** reject C0 control characters (`\0`, `\r`,
   `\n`, `\t`, `\x01`..`\x1F`, `\x7F`). A `folderNew` value containing e.g.
   `note\nbody=evil` passes validation and reaches
   `ArgumentList.Add("path=" + value)`. `ArgumentList` prevents *shell*
   injection (no `cmd.exe` involved, args are passed as a CreateProcess
   lpCommandLine with .NET's standard escaping), but the embedded newline is
   delivered verbatim to the Obsidian CLI's own `key=value` arg parser. Impact
   is bounded — Windows filesystems reject the characters so `create` fails,
   log lines stay safe because everything written to `FileLog` is sanitised
   (v2 F-03) — but the input should still be rejected at the validator.
3. **LOW — F-12 (still open).** `OBSIDIAN_CLI` override still accepts UNC /
   reparse-point targets. Same trust boundary as v2; unchanged.

---

## Verification table (F-01..F-15)

| ID | v2 sev | v3 Status | Evidence / delta |
|----|--------|-----------|------------------|
| F-01 | CLOSED | **still CLOSED** | `rg obsidiandev` → only hits are in `audit-reports/` (v1+v2 historical refs). `tools\New-DevCert.ps1` + `tools\Sign-DevMsix.ps1` + `Makefile:pack-signed` unchanged from v2. No new literal secret introduced. |
| F-02 | CLOSED | **still CLOSED** | `ObsidianCli.cs:26` still `WindowsPathExtensions = [".com", ".exe"]`. Resolution order (override → known install → registry → PATH) unchanged; one-shot PATH warning still gated by `Interlocked.Exchange(ref s_warnedPathResolution, …)`. |
| F-03 | CLOSED | **still CLOSED** | `FileLog.SanitizeForLogLine` remains the single chokepoint; called from every write path. No new `File.AppendAllText` / `WriteLine` direct-to-log sites introduced. Any taint from `folderNew` that reaches `_log.Warn(...)` (e.g. `create reported error on stdout: <folderNew>`) is scrubbed. |
| F-04 | OPEN (MED) | **still OPEN** | `FolderPathValidator.cs:34-51` unchanged — segments loop still only rejects exact `.`/`..`. `folderNew` now makes this reachable via a free-text card input (previously only the dropdown-selected `folder` hit the validator). Severity-of-interest rises even though the code didn't change. |
| F-05 | OPEN | **still OPEN** | `Program.cs:20-28` proof-of-life writer to `%UserProfile%\ObsidianWidget-proof.log` unchanged. |
| F-06 | OPEN | **still OPEN** | `JsonStateStore.ReadAllText` — unchanged. |
| F-07 | OPEN | **still OPEN** | `JsonStateStore` bare `catch` — unchanged. |
| F-08 | OPEN | **still OPEN** | `Directory.CreateDirectory` with inherited ACLs — unchanged. |
| F-12 | OPEN | **still OPEN** | `OBSIDIAN_CLI` UNC/reparse acceptance — unchanged. |
| F-13 | INFO | **still CLOSED** | Registry probe defensiveness — unchanged. |
| F-14 | INFO | **still CLOSED** | `IObsidianCliEnvironment` seam cannot forge log lines — unchanged. |
| F-15 | INFO | **still CLOSED** | `signtool /p` command-line password residual — unchanged. |

---

## New findings introduced / surfaced by `folderNew`

### F-16 (LOW · new) — `FolderPathValidator` accepts C0 control characters — CWE-20, CWE-74 (SUSPECTED)

**Taint path.** `ObsidianWidgetProvider.cs:251` reads the adaptive-card input:
```csharp
var folderNew = inputs.GetValueOrDefault("folderNew")?.Trim();
var folder = !string.IsNullOrEmpty(folderNew)
    ? folderNew
    : inputs.GetValueOrDefault("folder") ?? state.LastFolder;
```
`.Trim()` strips leading/trailing whitespace but leaves embedded control
characters intact. The value flows into `NoteRequest.Folder`, then
`FolderPathValidator.Validate` (`FolderPathValidator.cs:19`). The segment
illegal-char set is:
```csharp
private static readonly char[] IllegalSegmentChars = { ':', '*', '?', '"', '<', '>', '|' };
```
No `\r`, `\n`, `\t`, `\0`, or other `char.IsControl(ch)` rejection. A value
such as `foo\nbar` passes → `NoteCreationService:75` concatenates it with
the stem into `targetPath` → `ObsidianCli.CreateNoteAsync:137` builds
`"path=" + vaultRelativePath` and appends it via `ProcessStartInfo.ArgumentList`.

**Exploitability.**
- **Shell injection:** *not* exploitable. `ProcessStartInfo.UseShellExecute
  = false` + `ArgumentList` produces a `CreateProcess` lpCommandLine with
  .NET's quoting rules; no `cmd.exe`/`/bin/sh` is interposed. Confirmed by
  manual read of `ObsidianCli.cs:47-58`.
- **CLI arg-parser confusion:** the Obsidian CLI receives a *single* argv
  element `path=foo\nbar`. Whether it treats the embedded `\n` as a literal
  filename character or as a record terminator is implementation-defined.
  On Windows NTFS the literal control char would be rejected by the OS on
  create, so the CLI is expected to return an `Error:` line — which we log
  (via the sanitised `FileLog`) and surface as `CreationFailed`.
- **Log-line injection:** *not* exploitable — `FileLog.SanitizeForLogLine`
  (F-03) converts every C0 char to `\uXXXX` before write.
- **Path traversal:** *not* exploitable via control chars alone — `..`/`.`
  check is on trimmed segments, and there's no decoding step that would
  convert a control char into a `/` or `\`.
- **Filename smuggling on non-Windows:** ext4 permits most C0 bytes in file
  names. If a future build ships a cross-platform tray-only mode (the
  `ObsidianQuickNoteTray` project exists), a note with an embedded `\n` in
  its *folder* path could produce a directory that shells mis-render,
  aiding social-engineering on multi-user hosts. Marked **SUSPECTED**
  because today's attack surface is Windows-only.

**Severity: LOW.** Defence-in-depth; no confirmed primitive today.

**Fix sketch (one-liner).** Extend the segment scan:
```csharp
foreach (var ch in seg)
    if (char.IsControl(ch))
        return FolderValidationResult.Fail($"Control character in folder segment '{seg}'");
```
Or equivalently add every `char` < `0x20` plus `0x7F` to `IllegalSegmentChars`.
A regression test (`FolderPathValidator_RejectsControlChars`) should pin
`\r`, `\n`, `\t`, `\0`, `\x01`, `\x7F`.

### F-17 (INFO · new) — `CreateNoteAsync` uses `ArgumentList` — no path-traversal via arg-injection

`ObsidianCli.cs:47-58` builds `ProcessStartInfo` with `UseShellExecute=false`
and populates `psi.ArgumentList.Add(a)` per arg. No code path concatenates
user input into `ProcessStartInfo.Arguments` (the string form). `rg -n
"\.Arguments\s*="` over `src/` → zero matches. Conclusion: the *process
invocation* layer is not a traversal or injection sink — any folder-escape
must come through (a) the validator letting `..` through (v1 confirmed it
doesn't) or (b) the Obsidian CLI itself resolving `path=` above the vault
root (out of our trust boundary). Positive finding, no action.

### F-18 (INFO · new, SUSPECTED) — TOCTOU via intra-vault reparse point — CWE-367, CWE-59

`NoteCreationService.CreateAsync:67-77` resolves `vaultRoot`, checks
`File.Exists(full)` for duplicate-rename decisions, then invokes
`obsidian create path=<folder>/<stem>.md`. The existence check happens in
the widget host process; the actual file write happens in the Obsidian
process. If an attacker can pre-plant a directory junction or symlink
**inside** the vault (`<vault>\Inbox` → `C:\Users\victim\Documents`),
the Obsidian CLI will follow it and write outside the vault. This is a
*pre-existing* vault-trust issue (anyone who can write the vault tree
already has equivalent capability) and is not new to `folderNew`, but the
free-text folder box makes it reachable without the attacker having to
pre-seed the vault with an attacker-named folder visible in the dropdown.
**SUSPECTED · INFO.** Mitigations if we ever treat the vault as
semi-trusted: `FileAttributes.ReparsePoint` check on every
`Path.Combine(vaultRoot, folder)` before calling `obsidian create`, or
refuse folders whose realpath escapes `vaultRoot`.

---

## `folderNew` threat-model — explicit non-findings

Each item below was walked end-to-end; no exploitable primitive found. Listed
so a future auditor can see what was considered and skip the re-walk.

| # | Vector | Status | Why safe |
|---|--------|--------|----------|
| N-1 | `../../../outside-vault/foo` | safe | `Replace('\\','/')` → split on `/` → segments `..` rejected exactly by `FolderPathValidator.cs:40`. |
| N-2 | `..\..\outside` (backslash traversal) | safe | Line 24 `Replace('\\','/')` normalises before split; same rejection. |
| N-3 | `C:\Windows\System32\foo` (drive-letter absolute) | safe | `FolderPathValidator.cs:27` rejects any input with `normalized[1] == ':'`. |
| N-4 | `//server/share/evil` (UNC-style) | safe | After trim, first segment is `server`; no escape. Drive-letter check only matters for `X:`; there is no `\\?\…` bypass because `?` is in `IllegalSegmentChars`. |
| N-5 | `\\?\C:\x` (Win32 device-namespace prefix) | safe | Becomes `//?/C:/x` → `?` segment contains `?` → rejected. |
| N-6 | Device names `CON`, `NUL`, `COM1`, `LPT1`, `AUX`, `PRN` | safe | `FilenameSanitizer.IsReservedWindowsName` called from `FolderPathValidator.cs:46` rejects these (including with dot-extension). |
| N-7 | Null byte embedded in segment (`foo\0bar`) | safe-ish | Not in `IllegalSegmentChars` but .NET's `ArgumentList` / `CreateProcess` marshals each arg as a C string — downstream behaviour is arg-truncation at best. Covered by F-16 fix. |
| N-8 | CRLF in segment (`foo\r\nbar`) | safe | Same as N-7. `FileLog` sanitises any log echo (F-03 closed). Covered by F-16 fix. |
| N-9 | Unicode homoglyph slashes (U+2044 `⁄`, U+FF0F `／`) | safe | Not path separators on Windows/Linux; remain as literal characters inside a single segment. No traversal. (Some of them *are* rendered as `/` in fonts — cosmetic only.) |
| N-10 | Trailing dot / trailing space (`foo.`, `foo ` ) | safe | Explicitly rejected at `FolderPathValidator.cs:49`. |
| N-11 | NTFS alternate data stream (`foo:stream`) | safe | `:` in `IllegalSegmentChars` → rejected. |
| N-12 | Very long path (> 260 chars) | safe | Not a security issue; Obsidian CLI / Windows long-path handling governs. Worst case: `create` errors and we log via sanitiser. |
| N-13 | Shell metacharacters (`foo && calc`, `foo | evil`, `` foo`cmd` ``, `foo;evil`) | safe | `|` in `IllegalSegmentChars` rejects pipe. `&`, `;`, backtick, `$(…)` all pass the validator **but** `ArgumentList` puts them in one argv element and there is no shell interpreter → no injection. F-17 covers. |
| N-14 | `path=…` key smuggling (value `foo path=evil`) | safe | The entire string `"path=" + vaultRelativePath` is a *single* argv element. The Obsidian CLI does not re-split argv elements on whitespace. |
| N-15 | Leading-dot segments (`.obsidian`, `.git`, `.trash`) | **UNSAFE** — see F-04 | Validator accepts; this is the carry-over MED. |

---

## Trust boundaries — delta vs v2

| # | Source | Delta | Notes |
|---|--------|-------|-------|
| B8 | `folderNew` adaptive-card input | **new** | Free-text, taint-traced above. Control-char gap = F-16; leading-dot gap = F-04. No new sink categories introduced. |

No other boundaries changed between v2 and v3.

---

## Secret-leak triage (v3)

`rg -n "obsidiandev|BEGIN (RSA |EC |OPENSSH |DSA |)PRIVATE KEY|AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9]{36}" .` → 5 hits, all in `audit-reports/**/security-auditor.md` (historical references to the retired literal). **Clean.**

---

## Handoff list

- `bug-hunter` / next `security-auditor` round: F-04 (leading-dot) remains
  the highest-leverage outstanding item; F-16 is the cheapest fix (one
  `char.IsControl` guard). Both belong in the same PR.
- `test-author`: add two regression tests before the fix lands —
  `FolderPathValidator_RejectsLeadingDotSegments` and
  `FolderPathValidator_RejectsControlChars` (cover `\r`, `\n`, `\t`, `\0`,
  `\x01`, `\x7F`).
- `release-engineer`: still owns the optional WinVerifyTrust hook (retires
  F-02 TODO + F-12 simultaneously).
- `dependency-auditor`: no new code-level dep misuse found.

---

## Appendix — commands run

| Command | Result |
|---------|--------|
| `rg -n folderNew` | 4 files: `ObsidianWidgetProvider.cs` (line 251), two `QuickNote.*.json` card templates, `CardTemplatesTests.cs`. Single ingestion site; confirmed. |
| `rg -n "\.Arguments\s*="` under `src/` | 0 hits. No `ProcessStartInfo.Arguments` string concatenation anywhere. |
| `rg -n "ArgumentList\.Add"` under `src/` | 1 hit — `ObsidianCli.cs:58`. Single process-invocation sink. |
| `rg -n "IllegalSegmentChars"` | 2 hits (decl + use) — confirms no control-char entry. |
| `rg -n "obsidiandev\|BEGIN .*PRIVATE KEY\|AKIA\|ghp_"` | Only historical audit-report hits. Clean. |
| Manual review | `ObsidianWidgetProvider.cs` (input plumbing), `NoteCreationService.cs`, `FolderPathValidator.cs`, `FilenameSanitizer.cs`, `DuplicateFilenameResolver.cs`, `ObsidianCli.cs`, `ObsidianCliParsers.cs`. |
