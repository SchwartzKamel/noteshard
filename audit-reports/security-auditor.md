# Security Auditor — ObsidianQuickNoteWidget

**Archetype:** `security-auditor` (adversarial, code-level). **Mode:** READ-ONLY.
**Target:** `C:\Users\lafia\csharp\obsidian_widget`
**Scope:** `src/**`, `tests/**`, `tools/**`, `Makefile`, `.gitignore`, `.github/**`, `README.md`. Binary/build output (`bin/`, `obj/`, `AppPackages/`) excluded from source review but spot-checked for leaked material.
**Tools run:** manual taint analysis + ripgrep pattern scan. No new scanners installed (per guardrails).

---

## Executive summary

| Severity | Count |
|----------|------:|
| CRITICAL | 0 |
| HIGH     | 1 |
| MEDIUM   | 3 |
| LOW      | 4 |
| INFO     | 3 |

**Top 3 risks**

1. **HIGH — Hardcoded dev-cert password (`obsidiandev`) documented in-repo** (CWE-798). Trivial code-signing forge if the dev PFX is ever reused for a release or leaks from `%LocalAppData%`.
2. **MEDIUM — Untrusted PATH resolution for `obsidian` executable** (CWE-426 / CWE-427). First-match walk of `PATH` with `.cmd`/`.bat` extensions enabled; a writable PATH entry gives code execution in every user context that launches the widget/tray.
3. **MEDIUM — Log injection via unescaped tainted fields** (CWE-117) in `FileLog`. Verb, widget id, folder name, exception messages, and CLI stderr are written raw; attacker-influenced data can forge log entries.

---

## Trust boundaries enumerated

| # | Source | Surface | Reaches |
|---|--------|---------|---------|
| B1 | Windows Widget Host (COM) | `ObsidianWidgetProvider.OnActionInvoked.Data` JSON, `Verb`, `WidgetId` | state store, logs, CLI args |
| B2 | End user via Adaptive Card / tray form | `title`, `folder`, `body`, `tagsCsv`, `template`, `autoDatePrefix`, `openAfterCreate`, `appendToDaily` | sanitizer/validator → CLI → filesystem |
| B3 | Windows clipboard | `pasteClipboard` verb, tray hotkey seed | note body → CLI `content=` |
| B4 | Filesystem | `state.json` (`%LocalAppData%\ObsidianQuickNoteWidget\state.json`), `log.txt`, dev PFX, `ObsidianWidget-proof.log` (`%UserProfile%`) | deserialized into `WidgetState` dictionary |
| B5 | `PATH` env var | `obsidian.exe\|cmd\|bat` lookup in `ObsidianCli.ResolveExecutable` | subprocess execution |
| B6 | External process (`obsidian` CLI) stdout/stderr | `ParseVaultPath`, `ParseFolders`, log output | UI dropdown, filesystem existence check, log |

Network, HTTP, SQL, template engines, and deserializers beyond `System.Text.Json<Dictionary<string,WidgetState>>` are **not present** in this codebase.

---

## Findings

| Sev | # | Title | CWE | OWASP | Location |
|-----|---|-------|-----|-------|----------|
| HIGH | F-01 | Hardcoded dev-cert password `obsidiandev` in docs | CWE-798 | A07:2021 Identification & Authn Failures | `README.md:60-63`, `.github/copilot-instructions.md:37` |
| MED | F-02 | Untrusted PATH resolution of `obsidian` executable (`.cmd`/`.bat` accepted) | CWE-426, CWE-427 | A08:2021 Software & Data Integrity Failures | `src/…/Cli/ObsidianCli.cs:21, 157-171` |
| MED | F-03 | Log injection via unescaped tainted input | CWE-117 | A09:2021 Logging Failures | `src/…/Logging/FileLog.cs:28-42`; callers in `ObsidianWidgetProvider.cs:54, 67, 76, 86, 117, 137, 148, 253, 282, 319, 330` |
| MED | F-04 | FolderPathValidator allows dot-prefixed segments (`.obsidian/`, `.trash/`) | CWE-73 | A01:2021 Broken Access Control | `src/…/Notes/FolderPathValidator.cs:35-51` |
| LOW | F-05 | Proof-of-life log writes process args + pid to `%UserProfile%\ObsidianWidget-proof.log` | CWE-532 | A09:2021 | `src/ObsidianQuickNoteWidget/Program.cs:20-28` |
| LOW | F-06 | Unbounded `state.json` read (no size cap) — local DoS | CWE-400, CWE-502 | A05:2021 Security Misconfig | `src/…/State/JsonStateStore.cs:61-74` |
| LOW | F-07 | `JsonStateStore.Load` swallows all exceptions → silent reset of user state | CWE-754 | A09:2021 | `src/…/State/JsonStateStore.cs:70-73` |
| LOW | F-08 | Tray / widget log/state files world-readable at `%LocalAppData%` defaults | CWE-276 | A01:2021 | `src/…/Logging/FileLog.cs:19-21`, `src/…/State/JsonStateStore.cs:28-30` |
| INFO | F-09 | Obsidian CLI `content=` escape + `ProcessStartInfo.ArgumentList` — **not** vulnerable to arg injection | — | — | `src/…/Cli/ObsidianCli.cs:41-52, 131-133`; `src/…/Cli/ObsidianCliParsers.cs:54-63` |
| INFO | F-10 | `System.Text.Json` deserialization in `JsonStateStore` is type-safe (no polymorphism, no converters) | — | — | `src/…/State/JsonStateStore.cs:11-15, 67` |
| INFO | F-11 | COM `ClassFactory` exposes only `IWidgetProvider` / `IWidgetProvider2` to local OOP callers; no attacker-reachable RPC | CWE-862 (SUSPECTED) | — | `src/…/Com/ClassFactory.cs`, `src/ObsidianQuickNoteWidget/Program.cs` |

---

### F-01 (HIGH) Hardcoded dev-cert password in repo — CWE-798

**Snippet (`README.md:60-63`)**
```
Sideloaded MSIX builds need a trusted dev cert. One is generated at
`%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\dev.pfx` (password `obsidiandev` …
signtool sign /fd SHA256 /a /f ...\dev.pfx /p obsidiandev <path-to-msix>
```
Same string in `.github/copilot-instructions.md:37`.

**Exploit hypothesis.** The password `obsidiandev` is committed and public. If (a) the generating script produces a deterministic PFX, (b) a developer accidentally reuses this PFX for a **signed release** (the `Makefile:pack-signed` target reads `SIGNING_CERT`/`SIGNING_PASSWORD` from env, but nothing forbids pointing it at `dev.pfx`), or (c) the PFX ever leaks from `%LocalAppData%` (roaming profiles, backups, malware, triage capture), any attacker can mint MSIX packages that chain to the same "trusted" CN and be installed with zero additional prompts on machines that trusted the dev cert. Impact: arbitrary code execution under the victim's user, masquerading as "ObsidianQuickNoteWidgetDev." Also, `%LocalAppData%\...\dev-cert\dev.pfx` is **not** in `.gitignore` because the path is outside the repo — but developers who copy it into the repo for any reason would also leak the key, which the committed password renders instantly usable.

**Fix recommendation.**
1. Treat `dev.pfx` as secret. Have the generator script set a **per-developer random password** (e.g., `[RandomNumberGenerator]::GetBytes(16) → base64`) and print it once; never hard-code.
2. Remove the literal password from `README.md` and `.github/copilot-instructions.md`; replace with instructions like `/p "$env:OBSIDIAN_DEV_CERT_PASSWORD"`.
3. In `Makefile:pack-signed`, fail fast if `SIGNING_CERT` points at a file under `…\dev-cert\` (defence-in-depth against dev→prod reuse).
4. Confirm the dev cert's Extended Key Usage is **Code Signing** only, and issued with a short (≤90-day) validity. Rotate once after removing the literal password.
5. Add a repo-root `.gitignore` rule `**/dev-cert/`, `**/*.pfx` — both already globally matched by line 21 of `.gitignore`, but add an explicit negative guard plus a pre-commit hook running `gitleaks` to catch "obsidiandev" should it be re-introduced.

---

### F-02 (MEDIUM) Untrusted PATH resolution — CWE-426 / CWE-427

**Snippet (`Cli/ObsidianCli.cs:21, 157-171`)**
```csharp
private static readonly string[] WindowsPathExtensions = [".exe", ".cmd", ".bat", ""];
…
foreach (var dir in pathVar.Split(Path.PathSeparator))
{
    if (string.IsNullOrWhiteSpace(dir)) continue;
    foreach (var ext in exts)
    {
        var candidate = Path.Combine(dir, ExecutableName + ext);
        if (File.Exists(candidate)) return candidate;
    }
}
```

**Exploit hypothesis.** Any directory on the user's `PATH` that is writable by a lower-privilege actor (historically `C:\ProgramData\<vendor>\bin`, user-local `%LocalAppData%\…\bin` inserted by some installers, or a stale entry pointing to a removable drive) lets an attacker plant `obsidian.cmd` / `obsidian.bat` / `obsidian.exe`. On every note creation the widget invokes the planted binary with attacker-irrelevant arguments but **user-supplied content** in stdin-equivalent positions — the attacker gets code execution under the victim's user context each time a note is saved or folders are refreshed (every 2 minutes via `FolderRefreshInterval`). `.cmd`/`.bat` extensions widen the attack surface because a batch file is trivially authored and cmd.exe applies meta-character interpretation to its *own* command line, even though our args go through `ArgumentList` (the shell file itself is attacker-controlled content, so escaping at our layer does not help).

The same resolution happens in the **COM-hosted** widget process, which the Widget Host auto-starts; persistence is effectively automatic.

**Fix recommendation.**
1. Prefer a known-good location: check `%LOCALAPPDATA%\Programs\obsidian\obsidian.exe`, `%ProgramFiles%\Obsidian\Obsidian.exe`, and the registry `HKCU\Software\Classes\obsidian\shell\open\command` before falling back to `PATH`.
2. Drop `.cmd`/`.bat` from `WindowsPathExtensions`; require `.exe`.
3. After resolution, verify the Authenticode signature subject (expected: Obsidian.md Inc.) with `WinVerifyTrust` before spawning; refuse if unsigned and log.
4. Cache the resolved path once, and validate it still signs-clean before each spawn (cheap; the OS caches).

---

### F-03 (MEDIUM) Log injection — CWE-117

**Snippet (`Logging/FileLog.cs:28-36`)**
```csharp
private void Write(string level, string message)
{
    var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
    …
    File.AppendAllText(_path, line);
}
```
**Callers with tainted data (non-exhaustive):**
- `ObsidianWidgetProvider.CreateWidget`: `$"CreateWidget id={id} kind={widgetContext.DefinitionId} size={widgetContext.Size}"` — `id` and `DefinitionId` come from Widget Host but any process that can IPC with the provider (local packaged surface) can inject arbitrary strings.
- `ObsidianWidgetProvider.OnActionInvoked`: `$"OnActionInvoked id={id} verb={verb}"` — `verb` is attacker-controlled (JSON from the card action).
- `ObsidianWidgetProvider.HandleVerbAsync`: `$"Unknown verb: {verb}"` — ditto.
- `ObsidianCli.RunAsync`: `$"create failed (exit={r.ExitCode}): {r.StdErr.Trim()}"` — stderr from an external process (includes user-controlled title/body reflected by the CLI).
- `NoteCreationService.CreateAsync`: `$"Created {created}"` — `created` is the vault-relative path derived from user input.

**Exploit hypothesis.** An attacker who controls any of the tainted fields (trivially: the note title, the folder selector, or the JSON verb payload shipped through the Widget Host channel — tools like `widget-inspector`, WER dumps, or a malicious privileged-peer packaged app) can embed `\r\n` sequences to forge additional log entries. Given the log is the primary post-hoc forensic artefact for this product (and the README discusses troubleshooting via `log.txt`), forged entries can mask actual misuse or frame the user. No direct RCE, but this defeats incident-response assumptions.

**Fix recommendation.**
1. Sanitize every interpolated value through a single chokepoint before passing to `Write`: replace control chars (`\r`, `\n`, `\t`, `\0`, `\u0000`–`\u001f`) with `\x%02X` escapes, truncate to a reasonable max (e.g., 512 chars).
2. Alternatively switch to structured JSON logging (one JSON object per line) so that a forged `\n` cannot be mistaken for a new record by a downstream parser.
3. Add a unit test asserting `Write("INFO", "a\nfake [ERROR] x")` produces a single line in the file.

---

### F-04 (MEDIUM) Dot-prefixed folder segments accepted — CWE-73

**Snippet (`Notes/FolderPathValidator.cs:35-51`)**
```csharp
foreach (var seg in segments)
{
    if (string.IsNullOrWhiteSpace(seg)) return Fail("Empty folder segment");
    if (seg == "." || seg == "..") return Fail("Relative segments (./..) are not allowed");
    …
}
```
No rule rejects segments that *start* with `.` (only exact `.`/`..`).

**Exploit hypothesis.** User (or an attacker who smuggles `folder` through the action JSON) submits `folder=".obsidian/plugins/core"` and `title="data"`. `FilenameSanitizer.Sanitize("data")` → `"data"`. The resulting `path=.obsidian/plugins/core/data.md` is passed to `obsidian create`. If the CLI honours it, the user's Obsidian plugin/config directory receives attacker-controlled content, which can shape future Obsidian behaviour (not a sandbox escape, but an integrity violation inside a trust-sensitive directory). Similar risk for `.trash/`, `.git/` (if user vaults happen to be git repos), and vendor-specific dotfolders.

**Fix recommendation.** After segment validation, reject segments that start with `.` unless an explicit allowlist entry is added later. One-liner:
```csharp
if (seg[0] == '.') return Fail($"Folder segment '{seg}' cannot begin with '.'");
```
Add test coverage in `FolderPathValidatorTests`.

---

### F-05 (LOW) Proof-of-life log at `%UserProfile%\ObsidianWidget-proof.log` — CWE-532

**Snippet (`ObsidianQuickNoteWidget/Program.cs:20-28`)**
```csharp
var probePath = System.IO.Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "ObsidianWidget-proof.log");
System.IO.File.AppendAllText(probePath,
    $"{DateTime.Now:o} pid={Environment.ProcessId} args=[{string.Join(' ', args)}]{Environment.NewLine}");
```

**Exploit hypothesis.** Info leakage: writes to `%UserProfile%` (a location commonly included in crash reports, cloud-sync profiles like OneDrive Personal Vault, and migration bundles). Reveals use of the widget and its launch args, and also writes to a location that clearly was meant as a development sanity check ("Proof-of-life"). Low severity; high fix value.

**Fix recommendation.** Remove the probe or route it into `FileLog` under `%LocalAppData%` (already used by the app, covered by `.gitignore` tropes). Guard behind `#if DEBUG`.

---

### F-06 (LOW) Unbounded state.json read — CWE-400 / CWE-502

**Snippet (`State/JsonStateStore.cs:61-74`)**
```csharp
if (!File.Exists(_path)) return new Dictionary<string, WidgetState>();
var json = File.ReadAllText(_path);
var parsed = JsonSerializer.Deserialize<Dictionary<string, WidgetState>>(json, JsonOpts);
```

**Exploit hypothesis.** Any process running as the user can overwrite `state.json` with a multi-GB file. `File.ReadAllText` plus `JsonSerializer.Deserialize` into a `Dictionary<string, WidgetState>` will allocate proportional memory and spin CPU. The widget COM process (possibly under PLM hang detection) dies or slows. Not a confidentiality issue; the type map is concrete so polymorphic deserialization attacks (à la `TypeNameHandling`) are not possible — hence LOW.

**Fix recommendation.** Before read, check `FileInfo(_path).Length` and cap (e.g., 1 MB). Stream via `JsonSerializer.DeserializeAsync` from `FileStream` to cooperate with memory pressure. Reject individual `CachedFolders`/`RecentNotes` lists longer than a sane bound when loading.

---

### F-07 (LOW) `JsonStateStore.Load` swallows all exceptions — CWE-754

**Snippet (`State/JsonStateStore.cs:70-73`)**
```csharp
catch
{
    return new Dictionary<string, WidgetState>();
}
```

**Exploit hypothesis.** Corrupt or tampered state.json silently resets the user's configuration (RecentFolders, PinnedFolders, LastCreatedPath). An adversary with local write access can use this to erase forensic traces without generating any error signal. The same pattern exists in `Persist` (line 85-88).

**Fix recommendation.** Catch only `JsonException`, `IOException`, and `UnauthorizedAccessException`; log the exception (`_log.Error(...)`) so at least the fact of corruption is recorded. On corruption, rename the broken file to `state.json.corrupt.<timestamp>` instead of dropping it on the floor — gives the user a recovery path.

---

### F-08 (LOW) Permissions on per-user state/log directories — CWE-276

**Snippet (`FileLog.cs:19-21`, `JsonStateStore.cs:28-30`)** — `Directory.CreateDirectory(Path.GetDirectoryName(_path)!)` creates under `%LocalAppData%\ObsidianQuickNoteWidget\` inheriting ACLs from the parent (by default: `USER`, `SYSTEM`, `Administrators`). **Behaviour is correct for single-user boxes** but on shared Windows terminal-server hosts, default `%LocalAppData%` ACLs already isolate per user; no action required there.

**Exploit hypothesis.** On misconfigured systems where `%LocalAppData%` is on a redirected/roaming profile with loose ACLs, `state.json` and `log.txt` could be readable by other principals, leaking vault paths, recent-notes titles, and clipboard snippets (if they ended up in log text via exception messages reflecting the body).

**Fix recommendation.** Explicitly tighten the directory ACL on first creation using `DirectorySecurity` to owner-only. Redact `body` and `title` from log lines (reinforces F-03 fix).

---

### F-09 (INFO) Obsidian CLI argument injection — not exploitable

The CLI invocation uses `ProcessStartInfo.ArgumentList.Add(a)` (line 52, `ObsidianCli.cs`) with `UseShellExecute = false`. Each list entry is quoted per Windows `CommandLineToArgvW` rules by the .NET runtime. A `content=` body containing `\n`, spaces, quotes, or additional `key=value` pairs cannot escape the single argv slot. The `EscapeContent` transform is defensive (protects against the CLI's *own* interpretation of `\n`/`\t`) and correctly escapes `\` first (per the code comment). No injection path observed. If the code is ever refactored to a string command line, this becomes critical — add a regression test pinning the use of `ArgumentList`.

### F-10 (INFO) Deserialization is type-safe

`System.Text.Json` with `Dictionary<string, WidgetState>` and no custom `JsonConverter`, no `TypeInfoResolver` shenanigans, no polymorphism. `BinaryFormatter`, `DataContractSerializer`, `XmlSerializer`, `NewtonsoftJson` with `TypeNameHandling`, `YamlDotNet` — none present. Safe.

### F-11 (INFO / SUSPECTED) COM surface

`SingletonClassFactory<T>` returns the `ObsidianWidgetProvider` for `IWidgetProvider` QI. Registration uses `CLSCTX_LOCAL_SERVER | REGCLS_MULTIPLEUSE`. No custom COM security descriptor (`CoInitializeSecurity`) is set, so it falls back to the process default — acceptable for a packaged widget with `praid:App` identity (only the local Widget Host is expected to activate it). This is a **SUSPECTED** finding: I did not validate end-to-end that no peer package can QI the factory through a crafted `CoCreateInstanceEx`. If the intent is "only Widget Host must call us," an explicit `CoInitializeSecurity(…, RPC_C_AUTHN_LEVEL_PKT, RPC_C_IMP_LEVEL_IDENTIFY, …, EOAC_NONE)` plus an `IAccessControl` ACL scoped to the widget-host SID would harden it.

---

## Secret-leak triage

| File | Line(s) | Secret | Action |
|------|---------|--------|--------|
| `README.md` | 60, 63 | Dev cert password `obsidiandev` | **Rotate:** regenerate PFX with a random password, update the generator script, then remove the literal string. After the change lands, `git filter-repo --replace-text` the old password out of history (only if the PFX ever leaves the dev's box). |
| `.github/copilot-instructions.md` | 37 | Same | Same action. |

No API keys, PEM blocks, connection strings, `.env`, or OAuth tokens found in the working tree.

`.gitignore` line 21 (`*.pfx`) and line 22 (`*.cer`) adequately cover certificates. `.gitignore` does **not** ignore `%LocalAppData%\ObsidianQuickNoteWidget\` — but that path is outside the repo, so no action needed. State files under `bin/` are indirectly covered by the `bin/` ignore. OK.

---

## Handoff list

- `dependency-auditor` — please run `dotnet list package --vulnerable --include-transitive` against the three projects and triage; code-level audit did not touch dependency CVEs.
- `test-author` — please add regression tests for: (a) `FolderPathValidator` rejects segments starting with `.` (F-04), (b) `ObsidianCli.RunAsync` uses `ArgumentList` not `Arguments` (F-09 pinning), (c) `FileLog.Write` neutralizes embedded `\r\n` (F-03).
- `doc-scribe` — please update `README.md` / `.github/copilot-instructions.md` to drop the literal `obsidiandev` password once F-01 is fixed and rotation is complete.
- `release-engineer` — verify the `pack-signed` Makefile target cannot be invoked with `SIGNING_CERT` pointing at the dev PFX; add a guard.

---

## Appendix — tools & commands

| Tool | Version | Exit | Findings |
|------|---------|-----:|---------:|
| ripgrep (`rg`) — secret pattern sweep | bundled | 0 | 1 (dev-cert password, see F-01) |
| ripgrep — `obsidiandev` literal | bundled | 0 | 3 matches (2 source docs, 0 in code) |
| manual taint + CWE mapping | — | — | 11 findings |
| `semgrep`, `gitleaks`, `trufflehog` | not installed | — | not run (per guardrails: no new scanners without approval) |
