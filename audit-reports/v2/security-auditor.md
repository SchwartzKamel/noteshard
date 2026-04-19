# Security Auditor v2 — ObsidianQuickNoteWidget

**Archetype:** `security-auditor` (adversarial, code-level). **Mode:** READ-ONLY.
**Target:** `C:\Users\lafia\csharp\obsidian_widget`
**Predecessor:** `audit-reports/security-auditor.md` (F-01..F-11).
**Scope (delta):** verification of fixes for F-01/F-02/F-03 + fresh scan of new attack
surface introduced by those fixes (`OBSIDIAN_CLI` env-var ingestion, registry probe,
`IObsidianCliEnvironment` test seam, `FileLog.SanitizeForLogLine`).
**Tools run:** manual taint analysis + ripgrep. No new scanners installed.

---

## Executive summary

| Severity | Count (v2) | Δ vs v1 |
|----------|-----------:|--------:|
| CRITICAL | 0 | — |
| HIGH     | 0 | −1 (F-01 closed) |
| MEDIUM   | 1 | −2 (F-02, F-03 closed; F-04 still open) |
| LOW      | 5 | +1 (F-12 new; F-05..F-08 unchanged) |
| INFO     | 4 | +1 (F-13 new positive finding on registry hardening) |

**Top 3 risks (v2)**

1. **MED — F-04 (still open).** `FolderPathValidator` accepts dot-prefixed segments
   (`.obsidian/`, `.trash/`, `.git/`). Allows attacker-influenced `folder` payloads
   to reach Obsidian's own config dirs via the CLI. No code change since v1.
2. **LOW — F-12 (new).** `OBSIDIAN_CLI` env var is honoured with only an
   existence check; UNC paths (`\\attacker\share\obsidian.exe`), reparse points,
   and symlinks/junctions are not rejected. Same trust boundary as the user
   environment, so impact is bounded — but the fix for F-02 narrowed PATH while
   widening this new explicit-override surface.
3. **LOW — F-05 (still open).** Proof-of-life log at
   `%UserProfile%\ObsidianWidget-proof.log` still writes pid + raw `args` on
   every Widget Host activation. Information disclosure to OneDrive/backup
   sweeps; trivial to remove.

---

## Verification table (F-01..F-08)

| ID | v1 sev | Status | Evidence |
|----|--------|--------|----------|
| F-01 | HIGH | **CLOSED** | `obsidiandev` literal eradicated from working tree (only the v1 audit report mentions it). `tools\New-DevCert.ps1` generates a 24-char CSPRNG password (`RandomNumberGenerator.GetBytes`) into `password.txt` with explicit user-only ACL (`SetAccessRuleProtection($true,$false)`, owner = current user, only USER + SYSTEM + Administrators ACEs). `tools\Sign-DevMsix.ps1` reads it at runtime; password never echoed; in-memory variables cleared in `finally`. `Makefile:pack-signed` (lines 124-134) hard-fails when `SIGNING_CERT` resolves under any `[\\/]dev-cert[\\/]` path before invoking `dotnet publish`. README.md:60-66 + 74 documents the new flow with no literal secret. |
| F-02 | MED | **CLOSED** | `ObsidianCli.cs:26` — `WindowsPathExtensions = [".com", ".exe"]` (no `.cmd`/`.bat`). `ResolveExecutable` (lines 213-258) prefers, in order: `OBSIDIAN_CLI` override → `%ProgramFiles%\Obsidian\Obsidian.{com,exe}` → `%LocalAppData%\Programs\Obsidian\Obsidian.{com,exe}` → registry `HKCU\Software\Classes\obsidian\shell\open\command` → PATH. PATH fallback emits a one-shot warning via `Interlocked.Exchange(ref s_warnedPathResolution, …)`. Tests `ObsidianCliResolutionTests.CmdAndBatInPath_AreRejected_EvenIfPresent`, `PathScanAcceptsComAndExeOnly_AndWarnsOnce`, `KnownInstallLocation_WinsOverPath`, `RegistryCommand_UsedWhenKnownInstallPathsMissing` cover the resolution order. The TODO comment at lines 24-25 acknowledges WinVerifyTrust as a follow-up but is **not** required to close F-02 as written. |
| F-03 | MED | **CLOSED** | `FileLog.cs:36-66` — `SanitizeForLogLine` is a single chokepoint that escapes `\r`, `\n`, and all C0 control characters (except TAB) plus `0x7F` to `\uXXXX`. Called from `Write` (line 70) — every `Info`/`Warn`/`Error` path goes through it; `Error` additionally pre-sanitizes the exception text on line 28 (defence in depth, harmless re-sanitization downstream). UTF-8 bytes ≥ 0x20 are preserved verbatim, so non-ASCII content is not mojibake'd. |
| F-04 | MED | **OPEN** | `FolderPathValidator.cs:34-51` — segment loop still only rejects exact `.`/`..`. No leading-dot guard. Recommended one-liner from v1 not applied. Severity unchanged. |
| F-05 | LOW | **OPEN** | `Program.cs:20-28` — proof-of-life writer to `%UserProfile%\ObsidianWidget-proof.log` is unchanged. |
| F-06 | LOW | **OPEN** | `JsonStateStore.cs:75-88` — `File.ReadAllText` with no length cap; no streaming deserialize. |
| F-07 | LOW | **OPEN** | `JsonStateStore.cs:84-87` — bare `catch { return new Dictionary…; }`. Still silently resets state and never logs. (Note: `JsonStateStore` does not yet hold an `ILog`, so this needs a small DI tweak.) |
| F-08 | LOW | **OPEN** | `FileLog.cs:18` and `JsonStateStore.cs:38` still use `Directory.CreateDirectory` with inherited ACLs. Acceptable on default `%LocalAppData%`, suspect on redirected profiles. |

---

## Fresh findings introduced (or surfaced) by the v1 fixes

### F-12 (LOW · new) — `OBSIDIAN_CLI` accepts UNC / reparse-point targets — CWE-73, CWE-426 (SUSPECTED)

**Snippet (`ObsidianCli.cs:213-219`)**
```csharp
var overridePath = env.GetEnvironmentVariable("OBSIDIAN_CLI");
if (!string.IsNullOrWhiteSpace(overridePath) && env.FileExists(overridePath))
{
    return overridePath;
}
```
And `DefaultObsidianCliEnvironment.FileExists` (line 26):
```csharp
public bool FileExists(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
```

**Exploit hypothesis.** `File.Exists` returns `true` for UNC paths
(`\\attacker-host\share\obsidian.exe`), for files behind directory junctions /
symlinks (resolved transparently by the OS), and for paths under reparse-point
mount points. The override deliberately wins over every other candidate. An
attacker who can write the user environment block (HKCU\Environment, login
script, package installer) gets persistent code execution under the user the
next time the widget COM server is activated — even with PATH locked down.
Same trust boundary as setting `PATH` itself, so this does **not** raise the
attacker capability bar; severity is **LOW**, marked **SUSPECTED** because no
WinVerifyTrust hook gates the resolved binary. The TODO at `ObsidianCli.cs:24-25`
already acknowledges this gap.

**Fix recommendation.**
1. Reject overrides whose root is UNC (`Path.IsPathFullyQualified` + check first
   two chars are `\\` or use `new Uri(path).IsUnc`). Or: require
   `Path.GetFullPath(overridePath)` to start with one of a small allowlist of
   roots (`%ProgramFiles%`, `%LocalAppData%\Programs`).
2. After resolution, call `WinVerifyTrust` on the chosen exe (Windows-only) and
   refuse to spawn anything not Authenticode-signed by `Obsidian.md Inc.` —
   covers PATH, registry, and `OBSIDIAN_CLI` simultaneously and fully retires
   F-02 + F-12.
3. Optionally: open the candidate with `FileShare.None` + `FileOptions.None` and
   inspect `File.GetAttributes()` for `FileAttributes.ReparsePoint`; refuse if
   set. Cheap defence-in-depth.

### F-13 (INFO · new positive) — Registry probe is correctly defensive

`DefaultObsidianCliEnvironment.GetObsidianProtocolOpenCommand` (`IObsidianCliEnvironment.cs:28-41`)
opens `HKCU\Software\Classes\obsidian\shell\open\command`, returns `null` if
the subkey is missing (`key?.GetValue(null)`), and swallows any exception
(corrupt hive, access denied, registry redirected) — returning `null` rather
than throwing. The downstream parser `ExtractExeFromRegistryCommand`
(`ObsidianCli.cs:280-293`) tolerates `null`/empty/whitespace, malformed input
(`as string` returns `null` if value type isn't `REG_SZ`/`REG_EXPAND_SZ`), and
unterminated quotes (returns `null`, falls through to PATH). The
`ObsidianCliResolutionTests.ExtractExeFromRegistryCommand_Parses` theory pins
all six edge cases, including `"unterminated` → `null`. **No malformed-value
log-and-continue path** exists; the resolver simply moves to the next
candidate. Good.

### F-14 (INFO) — `IObsidianCliEnvironment` test seam does **not** bypass log sanitization

The test `FakeEnv` substitutes only env-var, file-exists, and registry probes;
it cannot influence log content because the `ResolveExecutable` log emission
goes through the supplied `ILog` (`CapturingLog` in tests, `FileLog` in prod).
`FileLog.Write` *always* funnels through `SanitizeForLogLine` regardless of
caller. Tests that exercise the warning path (`PathScanAcceptsComAndExeOnly_AndWarnsOnce`)
use `CapturingLog` which never touches the file system, so they cannot
exfiltrate or forge entries. No bypass observed.

### F-15 (INFO) — `Sign-DevMsix.ps1` password handling — minor leakage residual

`tools\Sign-DevMsix.ps1:39` reads the password into a `$password` variable, then
passes it as a positional argument to `signtool /p $password`. On Windows the
command line of a child process is visible to other processes running as the
same user via `NtQueryInformationProcess(ProcessBasicInformation)` /
`Get-CimInstance Win32_Process`. Since the password is per-developer and the
PFX is per-user, an attacker who can already enumerate same-user processes
also already has read access to `dev.pfx` + `password.txt`, so this is **not**
a privilege boundary crossing. Marked INFO. **Optional hardening:** prefer
`signtool sign /csp <provider> /kc <containerName>` with the cert imported into
`Cert:\CurrentUser\My` (already done by `New-DevCert.ps1` line 66) and drop the
`/f /p` pair entirely — the PFX file + password file then become unnecessary
for routine signing.

---

## Trust boundaries — delta vs v1

| # | Source | Delta | Notes |
|---|--------|-------|-------|
| B5 | `PATH` env var | **narrowed** | Only `.com`/`.exe` accepted; one-shot warning emitted on PATH-fallback resolution. |
| B5a | `OBSIDIAN_CLI` env var | **new** | Wins over all other candidates. Accepts UNC + reparse points. See F-12. |
| B5b | `HKCU\Software\Classes\obsidian\shell\open\command` | **new** | Read-only probe; null/exception-tolerant; well-formed input parsing pinned by tests. See F-13. |
| B7 | dev-cert PFX + `password.txt` under `%LocalAppData%\…\dev-cert\` | **new** | Per-user random password, file ACL restricted to USER + SYSTEM + Administrators. PFX consumed only by `Sign-DevMsix.ps1`; `Makefile pack-signed` actively refuses dev-cert paths. See F-15 for residual command-line leak. |

---

## Secret-leak triage (v2)

`rg "obsidiandev" .` → only match is the v1 audit report (a historical
reference, not a live secret). No PFX, no PEM, no API keys, no `.env`, no
connection strings in the working tree. **Clean.**

The v1 report's recommendation to `git filter-repo --replace-text` the old
literal out of history applies **only if** the dev PFX ever left a developer's
machine; since the literal was a deterministic password rather than an actual
key, and the PFX is generated locally, history rewrite is optional. If
desired, the simple `git filter-repo --replace-text` against `obsidiandev` can
be done at any time and is non-blocking.

---

## Handoff list

- `bug-hunter` / next round of `security-auditor`: F-04, F-05, F-06, F-07,
  F-08 carry over from v1 — none of them block a release on their own but
  F-04 is the highest-leverage residual.
- `release-engineer`: consider implementing the WinVerifyTrust gate proposed
  in F-02's TODO; this would also retire F-12.
- `dependency-auditor`: still owns CVE triage of NuGet refs; out of scope here.

---

## Appendix — commands run

| Command | Result |
|---------|--------|
| `rg -n obsidiandev` (working tree) | 5 matches, all in `audit-reports/security-auditor.md` (v1). 0 in code/docs/tools. |
| `rg -n OBSIDIAN_CLI` | 6 matches: 3 in `ObsidianCli.cs` (override/log/doc), 3 in `ObsidianCliResolutionTests.cs`. |
| `rg -n IObsidianCliEnvironment` | 3 files: interface, prod resolver, test fakes. No prod call sites bypass it. |
| `rg -n SanitizeForLogLine` | 3 hits — all inside `FileLog.cs`. Confirms single chokepoint. |
| Manual review | `tools/New-DevCert.ps1`, `tools/Sign-DevMsix.ps1`, `Makefile:pack-signed`, `ObsidianCli.cs`, `IObsidianCliEnvironment.cs`, `FileLog.cs`, `JsonStateStore.cs`, `Program.cs`, `FolderPathValidator.cs`, `ObsidianCliResolutionTests.cs`. |
