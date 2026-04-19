# Security

> This page is for contributors making security-adjacent changes: follow-ups on F-series findings, touching the CLI / URI launcher, editing the folder validator or the logger, or rotating the dev cert. It summarizes the threat model and the status of every finding through v3.

Up: [`../README.md`](../README.md) (docs index)

Canonical source of truth:
[`../../audit-reports/v3/security-auditor.md`](../../audit-reports/v3/security-auditor.md).
This page summarizes; defer to that report when they disagree.

## F-series status at HEAD (1.0.0.7)

### Closed

| ID | Summary | Closed by |
| --- | --- | --- |
| **F-01** (HIGH; CWE-798) | Dev-cert password was a hardcoded literal. | Replaced with a per-developer 24-char random password generated on first `tools\New-DevCert.ps1` run, written to `%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\password.txt` with a user-only ACL (current user + SYSTEM + Administrators, inheritance disabled). Never echoed or committed. `tools\Sign-DevMsix.ps1` reads it at runtime; `make pack-signed` refuses any `SIGNING_CERT` under `dev-cert\`. 1.0.0.1. |
| **F-02** (MED; CWE-426/427) | PATH-based CLI discovery accepted `.cmd` / `.bat`, enabling PATH-hijack. | [`ObsidianCli`](../../src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs) resolution order is now override (`OBSIDIAN_CLI`) → known install locations (`C:\Program Files\Obsidian\Obsidian.{com,exe}`) → registry → PATH; `WindowsPathExtensions = [".com", ".exe"]` (line 28). One-shot PATH-fallback warning gated by `Interlocked.Exchange`. Follow-up TODO at `ObsidianCli.cs:26` — WinVerifyTrust on first spawn. 1.0.0.1. |
| **F-03** (MED; CWE-117) | CRLF / control-char log injection. | [`FileLog.SanitizeForLogLine`](../../src/ObsidianQuickNoteWidget.Core/Logging/FileLog.cs) is the single chokepoint: every C0 char except `\t` escaped (`\r`, `\n`, `\uXXXX`). Every write path routes through it. Taint from `folderNew` that reaches a `_log.Warn(...)` echo is scrubbed. 1.0.0.1. |
| **F-13** | Registry probe defensiveness. | Closed v2. |
| **F-14** | `IObsidianCliEnvironment` seam cannot forge log lines. | Closed v2. |
| **F-15** | `signtool /p` command-line password residual. | Closed v2 (password is only in-memory; `signtool` is invoked via PowerShell's argv, not a cmd string). |
| **F-16** (LOW; CWE-20/74) | `FolderPathValidator` accepted C0 control characters (`\r`, `\n`, `\t`, `\0`). | [`FolderPathValidator.cs:46-50`](../../src/ObsidianQuickNoteWidget.Core/Notes/FolderPathValidator.cs) now rejects any `char.IsControl(ch)` in a segment. 1.0.0.3. |

### Still open

| ID | Sev | Summary | Rationale |
| --- | --- | --- | --- |
| **F-04** | MED | `FolderPathValidator` accepts leading-dot segments (`.obsidian/`, `.git/`, `.trash/`). With `folderNew` a free-text input, a user can be tricked into pasting a config-tree path and having a note land in Obsidian's own config. Fix is a one-liner: reject segments matching `seg.StartsWith('.')`. | Not yet fixed — see handoff in v3 auditor report. |
| **F-05** | INFO | `Program.cs:20-28` writes a proof-of-life line to `%UserProfile%\ObsidianWidget-proof.log` on every COM-server launch. | Intentional for now as a diagnostic; a retired-on-ship TODO. |
| **F-06** | INFO | `JsonStateStore.ReadAllText` loads the full file into memory without a size cap. | State is bounded by widget count in practice; no DoS vector. |
| **F-07** | INFO | `JsonStateStore` has a bare `catch` swallowing malformed-JSON exceptions. | Deliberate — "widget must never crash over state persistence" — but it does mask deserialization regressions. |
| **F-08** | INFO | `Directory.CreateDirectory` under `%LocalAppData%` uses inherited ACLs. | Low impact (user-scoped); only matters if `%LocalAppData%` inheritance is already broken. |
| **F-12** | LOW | `OBSIDIAN_CLI` environment override accepts UNC and reparse-point targets. | Retiring this also retires the F-02 follow-up TODO (WinVerifyTrust check on first spawn). |
| **F-17** | INFO | `CreateNoteAsync` uses `ProcessStartInfo.ArgumentList` — no path-traversal via arg-injection. | Positive finding; no action. |
| **F-18** (SUSPECTED) | INFO | Intra-vault reparse-point TOCTOU in `NoteCreationService.CreateAsync`. | Vault is already a trust boundary (anyone who can write the vault has equivalent capability); optional mitigation would check `FileAttributes.ReparsePoint` before `obsidian create`. |

## Why `ProcessStartInfo.ArgumentList` everywhere

[`ObsidianCli.cs:60`](../../src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs):

```csharp
foreach (var a in args) psi.ArgumentList.Add(a);
```

Every argv token is passed as a separate list element. Under the hood .NET
marshals these into `CreateProcess`'s `lpCommandLine` with its own quoting
rules — **no `cmd.exe` / `/bin/sh` is interposed**. The string form
`ProcessStartInfo.Arguments` is never used; a ripgrep for
`\.Arguments\s*=` under `src/` returns zero hits (F-17). Shell metacharacters
(`&&`, `|`, backticks, `;`, `$(...)`) arrive at the Obsidian CLI as literal
characters inside a single argv element — no injection primitive.

When adding a new CLI call, **keep the argv-list discipline**. Do not
construct a single argument string with embedded spaces / quotes and pass it
through `Arguments`. Either the CLI already accepts `key=value` as a single
token (it does, for every verb we use) or split on a real delimiter and call
`ArgumentList.Add` per token.

## Why log sanitization matters

Any attacker-influenced value — `folderNew` text, CLI stdout with an
`Error:` line, an exception message — can contain `\r` / `\n` and forge
additional log lines. `FileLog.SanitizeForLogLine`
([`FileLog.cs:36`](../../src/ObsidianQuickNoteWidget.Core/Logging/FileLog.cs))
is the single chokepoint: every C0 char except `\t` is escaped
(`\r`, `\n`, `\uXXXX`). It's called on every `Info` / `Warn` / `Error` path.

**Do not add a direct `File.AppendAllText(LogPath, …)` / `StreamWriter.WriteLine`
site.** Route through `ILog`. If you need structured output that bypasses the
logger, audit every substitution for attacker-influenced values first.

## Folder path validation rules

[`FolderPathValidator.Validate`](../../src/ObsidianQuickNoteWidget.Core/Notes/FolderPathValidator.cs):

Rejects:

- Drive-letter absolute paths (`C:\…`, `C:/…`) — `normalized[1] == ':'`.
- Empty segments after normalization.
- `.` and `..` segments (traversal).
- `:`, `*`, `?`, `"`, `<`, `>`, `|` (Windows illegal; `|` also neuters any
  theoretical pipe-injection).
- C0 control characters in any segment (F-16).
- Windows reserved names (`CON`, `NUL`, `COM1`, `LPT1`, `AUX`, `PRN`, …,
  including `CON.md`) via
  [`FilenameSanitizer.IsReservedWindowsName`](../../src/ObsidianQuickNoteWidget.Core/Notes/FilenameSanitizer.cs).
- Segments starting/ending with a space, or ending with `.`.

Accepts (today, but flagged as F-04):

- Leading-dot segments like `.obsidian` / `.git` / `.trash`.

Normalizes:

- `\` → `/`.
- Strips leading/trailing `/`.
- An empty or `/` input maps to vault-root (returned as `""`).

The v3 threat-model walk covers 15 distinct attack vectors (`../…`, UNC-style,
`\\?\…`, homoglyph slashes, NTFS ADS, null byte, CRLF, long path, shell
metacharacters, `path=` key smuggling, …) and concludes the only unsafe one
is the leading-dot case (F-04). See
[`../../audit-reports/v3/security-auditor.md`](../../audit-reports/v3/security-auditor.md)
§"`folderNew` threat-model — explicit non-findings".

## URI launcher threat model (v7)

[`ObsidianLauncher`](../../src/ObsidianQuickNoteWidget.Core/Cli/ObsidianLauncher.cs)
(new in 1.0.0.7) shells `obsidian://open?vault=…&file=…` via
`Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true })`.
Sources of the URI components:

- **vault name** — resolved from `%APPDATA%\obsidian\obsidian.json` (the
  Obsidian-authored config file). Never taken from a widget card input.
- **file path** — for `openRecent`, comes from the `recents` list (CLI-authored
  and intersected with `obsidian files`, which is CLI-authored too); for
  `openVault`, there is no file component.

Pre-flight checks in [`ObsidianLauncher.IsSafeRelativePath`](../../src/ObsidianQuickNoteWidget.Core/Cli/ObsidianLauncher.cs):

- Rejects control characters (`c < 0x20 || c == 0x7f`).
- Rejects rooted paths (`Path.IsPathRooted(path)`).
- Rejects any `..` segment after normalizing separators.

Both `vault` and `file` are `Uri.EscapeDataString`-encoded before
concatenation, so any literal `&` / `?` / `#` in a path cannot inject query
parameters, fragments, or break out of the `file=` value.

The trust boundary is the Obsidian URI handler itself — it decides how to
resolve `file=` against the vault root. We intentionally do not re-implement
the resolution; we only prevent the trivial smuggling cases (absolute paths,
traversal, URL-parser confusion via un-encoded separators).

## Rotating the dev cert

Generate a fresh cert + password every 90 days or on any suspicion of
exposure:

```powershell
.\tools\New-DevCert.ps1 -Force
```

The subject stays `CN=ObsidianQuickNoteWidgetDev`, so the `.cer` already in
**Trusted People (LocalMachine)** stays valid. The old password is
overwritten in place; `password.txt`'s user-only ACL is re-applied.

Do not check any of `%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\*` into
source — the whole folder is git-ignored.

## See also

- [`development.md`](./development.md) — dev-cert setup.
- [`release.md`](./release.md) — signing pipeline + open blockers.
- [`cli-surface.md`](./cli-surface.md) — CLI invocation discipline (F-17).
- [`../../audit-reports/v3/security-auditor.md`](../../audit-reports/v3/security-auditor.md) — authoritative v3 audit.
- [`../../audit-reports/v3/release-engineer.md`](../../audit-reports/v3/release-engineer.md) — version-hygiene + public-publish blockers.

Up: [`../README.md`](../README.md)
