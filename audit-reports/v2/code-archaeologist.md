# Code Archaeologist — v2 Refresh

> **Archetype:** `code-archaeologist` (read-only, one-shot handoff).
> **Target:** `C:\Users\lafia\csharp\obsidian_widget` at `HEAD = d2cb0f6` (after audit-driven fixes + F-01/F-02/F-03 security hardening).
> **Delta scope:** this is a *refresh* of `audit-reports/code-archaeologist.md`. Only changed material is reproduced in full — sections that are unchanged are summarized with pointers back to v1.

## 1. Since v1 — what actually shipped

Two commits on `main`:

- `f12a196` — *Initial commit: widget board provider + audit-driven fixes.* Landed the bug-hunter B1/B2/B3 fixes, the widget-plumber `IsComServerMode` fix, the card-author Adaptive Cards 1.5 downgrade + `widgetId` threading, the manifest-surgeon `<AdditionalTasks/>` + version bump to `1.0.0.1`, and the doc-scribe/code-archaeologist cleanup (orphan `WidgetsDefinition.xml` deleted).
- `d2cb0f6` — *Security hardening F-01, F-02, F-03.* Dev-cert password per-developer (`tools/New-DevCert.ps1`, `tools/Sign-DevMsix.ps1`), `obsidian` resolution preference ladder with `.cmd`/`.bat` rejected from PATH, and `FileLog.SanitizeForLogLine` against CRLF/C0 injection.

Sources beyond the git log: `CHANGELOG.md:10-26`, `Makefile:…` (dev-cert targets added), `.gitignore` (dev-cert artifacts excluded).

## 2. Items from v1 that are resolved (remove from mental map)

| v1 reference | Status now | Evidence |
| --- | --- | --- |
| Gotcha 4 — `WidgetsDefinition.xml` mismatch | **Deleted.** No longer in tree. | `Get-Item src\ObsidianQuickNoteWidget\WidgetsDefinition.xml` → not found. CLSID/ID-sync gotcha drops from 4 → 3 locations. |
| Gotcha 4 knock-on — duplicated widget IDs in 3 files | Down to 2 files: `WidgetIdentifiers.cs:11-12` + `Package.appxmanifest:69,:91`. | `Package.appxmanifest:69,91`. |
| Tech-debt `Program.cs:110-111` — `IsComServerMode` dead branch | **Fixed.** Now correctly returns `true` only on `-Embedding` / `/Embedding` and falls through to a clean exit + user-facing message for CLI invocation. | `src/ObsidianQuickNoteWidget/Program.cs:35-41, :104-116`. |
| Bug-hunter B1 — `Get → mutate → Save` race | **Fixed** via `AsyncKeyedLock<string>` per widget id. Every state-mutation path on the provider acquires `_gate.WithLockAsync(id, …)`. | `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:33-34, :67-76, :86-91, :116-124, :133-140, :170-181, :203-213, :245-306, :342-352, :367-378`. Tests: `tests/.../PerWidgetGateTests.cs`. |
| Bug-hunter B2 — timer resurrects deleted widgets | **Fixed.** `RefreshAllActiveAsync` re-checks `_active.ContainsKey(id)` inside the gate before saving. | `Providers/ObsidianWidgetProvider.cs:367-378`. |
| Bug-hunter B3 — fire-and-forget swallowed exceptions | **Fixed** via `AsyncSafe.RunAsync`. `FireAndLog` wires `onError` to write `LastError` under the gate and trigger a `SafePushUpdate`. | `Core/Concurrency/AsyncSafe.cs:16-36`; `Providers/ObsidianWidgetProvider.cs:160-186`. Tests: `AsyncSafeTests.cs`. |
| Card-author — Adaptive Cards 1.5 / `widgetId` threading | Shipped (not re-verified here — see `audit-reports/card-author.md`). | `CHANGELOG.md:17-18`. |
| Manifest-surgeon — `<AdditionalTasks/>` + version `1.0.0.1` | Shipped. | `src/ObsidianQuickNoteWidget/Package.appxmanifest:14, :89, :110`. |
| cli-probe — exit-code success detection + `Created:` parsing + auto-rename path | Shipped. | `Core/Cli/ObsidianCli.cs:140-165`; `Core/Cli/ObsidianCliParsers.cs` `HasCliError` / `TryParseCreated`. |
| security-auditor F-01/F-02/F-03 | Shipped (see §3 below for the seam implications). | `Core/Cli/ObsidianCli.cs:20-28, :203-297`; `Core/Logging/FileLog.cs:30-66`; `tools/New-DevCert.ps1`, `tools/Sign-DevMsix.ps1`. |

## 3. New seams that reshape the architecture

Three new abstractions showed up that a new contributor must understand — they are *load-bearing* for the current concurrency and security model.

### 3.1 `AsyncKeyedLock<TKey>` — `Core/Concurrency/AsyncKeyedLock.cs:11`

- Reference-counted per-key async mutex. Semaphores are created on first `Acquire` and disposed when the last holder releases. Re-entrant callers on different keys run in parallel; same-key callers serialize.
- Owner: `ObsidianWidgetProvider._gate` (`Providers/ObsidianWidgetProvider.cs:33-34`). Comparer is `StringComparer.OrdinalIgnoreCase` — widget IDs are treated case-insensitively.
- **Non-goal:** cross-process exclusion. The tray has its own provider instance writing the same `state.json` under the key `"tray"`; there is no cross-process lock (`Core/State/JsonStateStore.cs:16-21` xmldoc is explicit about this).
- Test harness: `tests/.../PerWidgetGateTests.cs` pins down serialization, isolation across keys, cancellation behavior, and entry cleanup.

### 3.2 `AsyncSafe.RunAsync` — `Core/Concurrency/AsyncSafe.cs:16`

- Fire-and-forget helper that logs into `ILog` and optionally runs a caller-supplied `onError` — itself guarded. The returned `Task` *never* faults, so callers may `_ = AsyncSafe.RunAsync(…)` or `await` it in tests.
- Every fire-and-forget path in the provider goes through `FireAndLog` (`ObsidianWidgetProvider.cs:160-186`), which composes `AsyncSafe.RunAsync` with an `onError` that writes `state.LastError` under the gate and then re-pushes the card. Two direct uses remain for CLI shell-outs: `HandleVerbAsync` `openVault` (`:226`) and nothing else.
- Test harness: `AsyncSafeTests.cs`.

### 3.3 `IObsidianCliEnvironment` — `Core/Cli/IObsidianCliEnvironment.cs:7`

- *Internal* seam used exclusively by `ObsidianCli.ResolveExecutable(env, log)` (`Core/Cli/ObsidianCli.cs:213-258`). Provides `IsWindows`, `GetEnvironmentVariable`, `FileExists`, and `GetObsidianProtocolOpenCommand` (reads `HKCU\Software\Classes\obsidian\shell\open\command`).
- `DefaultObsidianCliEnvironment.Instance` is wired in the `ObsidianCli` ctor (`:35`). Tests inject `FakeEnv` (`tests/.../ObsidianCliResolutionTests.cs:9`) to drive each branch of the preference ladder.
- **Preference ladder** (F-02, xmldoc at `ObsidianCli.cs:203-212`): `OBSIDIAN_CLI` → `%ProgramFiles%\Obsidian\Obsidian.(com|exe)` → `%LocalAppData%\Programs\Obsidian\Obsidian.(com|exe)` → `HKCU\...\obsidian\shell\open\command` (quoted-or-unquoted parsing at `:280-293`) → PATH scan *restricted to `.com`/`.exe`* with a one-shot WARN via the static latch `s_warnedPathResolution` (`:28, :247-252`). `.cmd`/`.bat` are deliberately rejected — PATH-hijack / CWE-426/427 mitigation, rationale at `:20-25`.

### 3.4 `FileLog.SanitizeForLogLine` — `Core/Logging/FileLog.cs:36-66`

- CWE-117 mitigation: CR, LF, and C0 controls (except TAB) become printable escape literals (`\r`, `\n`, `\u00xx`). Applied to every log line at `:70` and to exception `ToString()` payloads at `:28`.
- Test harness: `FileLogTests.cs`.

### 3.5 `JsonStateStore` — contract clarified (no code change, new xmldoc)

- `Core/State/JsonStateStore.cs:8-21` now *explicitly* states: (a) individual `Get`/`Save`/`Delete` calls are serialized with `_gate`; (b) the read-modify-write sequence is **not** atomic at this layer — callers must wrap with `AsyncKeyedLock`; (c) cross-process writes between widget + tray are last-write-wins.
- This removes the ambiguity v1 flagged in Gotcha 15.

## 4. Updated Gotchas list

Supersedes v1 §10. Unchanged items kept verbatim for continuity; new or amended items marked **[NEW]** / **[CHANGED]**.

1. **STA + native Win32 pump is mandatory.** `Program.cs:15, :84-90` (unchanged).
2. **WinRT marshalling, not classic CCW.** `Com/ClassFactory.cs:50` (unchanged).
3. **[CHANGED] 3-location ID sync.** CLSID lives in `WidgetIdentifiers.cs:8`, `Package.appxmanifest:47` (`com:Class Id`), and `Package.appxmanifest:66` (`CreateInstance ClassId`). Widget definition IDs in `WidgetIdentifiers.cs:11-12` and `Package.appxmanifest:69, :91`. `WidgetsDefinition.xml` is gone, so the old 4th location is retired.
4. **Manifest uninstall-dance.** Unchanged (`README.md`, `.github/copilot-instructions.md`).
5. **CLI surface is positional `key=value`.** Unchanged (`Core/Cli/ObsidianCli.cs:127-201`).
6. **`content=` escaping order matters.** Unchanged (`Core/Cli/ObsidianCliParsers.cs`).
7. **`obsidian ls` does not exist.** Unchanged — use `obsidian folders`.
8. **`GetCurrentThreadId` is in `kernel32.dll`, not `user32.dll`.** Unchanged (`Com/Ole32.cs:56-57`).
9. **P/Invoke mixing on `CoRegisterClassObject`.** Unchanged (`Com/Ole32.cs:14-22`, `#pragma warning disable SYSLIB1054`).
10. **`Platforms = x64;x86;arm64`, `TargetPlatformMinVersion = 10.0.22621.0`.** Unchanged.
11. **Widget provider must never throw out of COM entry points.** Every inbound COM callback funnels through `FireAndLog`; `PushUpdate` and `SafePushUpdate` wrap everything in try/catch (`Providers/ObsidianWidgetProvider.cs:384-421`).
12. **`TreatWarningsAsErrors=true` globally.** `Directory.Build.props:5`. Unchanged.
13. **Tests cover `Core` only.** Unchanged; no UI/COM harness. Four new test files since v1: `AsyncSafeTests`, `PerWidgetGateTests`, `ObsidianCliResolutionTests`, `FileLogTests`.
14. **[NEW] CLI resolution is deliberately *not* a plain PATH lookup.** Editing `ObsidianCli.ResolveExecutable` must preserve the preference order from §3.3 and *must not* re-admit `.cmd`/`.bat`. The static `s_warnedPathResolution` latch is process-lifetime — tests call `ObsidianCli.ResetPathWarningForTests()` (`:296`) to isolate.
15. **[NEW] `AsyncKeyedLock` does not cross process boundaries.** The tray (`StateKey = "tray"`) and the widget host each hold their own lock. Cross-process atomicity is achieved *only* by partitioning widget ids — this is the reason the tray uses a fixed key that cannot collide with a real Widget Host–assigned GUID id.
16. **[NEW] `FireAndLog` error-path pushes a card update.** `onError` takes the gate, writes `LastError`, releases, then calls `SafePushUpdate`. If a caller already mutated state inside the work lambda and the lambda *then* threw, the error handler will read the partial state — this is by design (the gate serializes, so the partial write is durable). Do not add a second write in the lambda's `catch`; let `AsyncSafe` surface it.
17. **[NEW] Dev-cert password is per-developer.** Do not hard-code a password in `Makefile`/docs; `tools/New-DevCert.ps1` stores a machine-local, user-ACL'd password for `tools/Sign-DevMsix.ps1` to read. Rationale: `audit-reports/security-auditor.md` F-01; `CHANGELOG.md:22-23`.
18. **[NEW] Log lines are sanitized.** Any code path that logs *user-controlled* strings (CLI stdout/stderr, widget inputs) is safe against CWE-117 because `FileLog.Write` sanitizes at the sink (`FileLog.cs:70`). Do not pre-format with `\n` thinking it will render as multi-line — it will be escaped to `\\n`.

## 5. Tech-debt register — refreshed

### Resolved (remove from v1 register)

- ~~`src/ObsidianQuickNoteWidget/WidgetsDefinition.xml`~~ — deleted.
- ~~`Program.cs:110-111` — `IsComServerMode` always returns `true`~~ — fixed; now branches on `-Embedding`.

### Still open (carried from v1)

- `Program.cs:18-28` — proof-of-life log writer to `%UserProfile%\ObsidianWidget-proof.log`. Still present, still deliberately outside package redirection. Should be feature-flagged or removed for a Store submission build.
- `Com/Ole32.cs:12-22` — `#pragma warning disable SYSLIB1054` on `CoRegisterClassObject` will remain until `[LibraryImport]` marshaller supports IUnknown-marshalled `object`.
- `Providers/ObsidianWidgetProvider.cs:39, :51-55` — `_folderRefreshTimer` has no explicit dispose. Unchanged — xmldoc now acknowledges the lifecycle (`:36-38`): "tied to COM host process — no explicit dispose". Brittle if the provider is ever instantiated outside the host.
- `Core/State/JsonStateStore.cs:105-106` — `Clone` via JSON round-trip on every `Get`/`Save`. Unchanged; flagged by `perf-profiler`.
- `Core/Logging/FileLog.cs:78-81, :95` — catch-all `catch { }` in `Write` and `Roll`. Unchanged; by design (logger must never throw).
- `src/ObsidianQuickNoteTray/GlobalHotkey.cs:29` + `Program.cs:42-53` — fixed hotkey id `0x9001`, hard-coded `Ctrl+Alt+N`. Unchanged.
- `tools/WidgetCatalogProbe.cs` + `tools/AppExtProbe/` — still undocumented beyond `.github/copilot-instructions.md:48`.

### New debt accumulated since v1

- **`ObsidianCli.cs:24-25` — `TODO(F-02 follow-up)` Authenticode signature check.** Written as a comment, not in any tracker. Until done, an attacker who can drop an `Obsidian.exe` into `%ProgramFiles%\Obsidian\` (admin-only, so low prior) wins over a real install.
- **`ObsidianCli.cs:28, :247-253, :296` — `s_warnedPathResolution` is a *static* one-shot.** Process-wide; tests must call `ResetPathWarningForTests()` between tests. If the provider is ever refactored to allow multiple `ObsidianCli` instances in the same process (e.g., a mock for integration tests), the "one warning per process" contract may silently drop warnings from the real instance.
- **`ObsidianWidgetProvider.cs:312-316` — post-create `_store.Get(session.Id)` read happens *outside* the gate.** The comment at `:308-311` acknowledges the choice ("trust the just-written state"). In single-process operation this is fine; if the tray writes between `await` points, the widget's refresh-trigger decision uses a stale `LastError`. Latent.
- **`ObsidianWidgetProvider.FireAndLog` — two layers of `ContinueWith` per invocation** (`:182-185`). Readable but allocates; every inbound COM callback goes through it. Measure before worrying.
- **`AsyncKeyedLock<TKey>` is never disposed and retains no OS handles, but its internal semaphore creation on first access means a `cancel before Acquire` path would still have to release.** `WithLockAsync` handles this correctly (`AsyncKeyedLock.cs:33-37`) — just be aware when extending.
- **`tools/New-DevCert.ps1` + `tools/Sign-DevMsix.ps1`** — new code paths in the dev pipeline. Password storage uses user-only ACL; verify on future Windows builds (CryptProtectData / DPAPI semantics) during CI changes. See `audit-reports/security-auditor.md`.
- **`Makefile` dev-cert targets** — now mandatory for a local package build; missing documentation on first-run (`README.md:…` covers it but drift is likely). `doc-scribe` should keep this in sync on next sweep.
- **`JsonStateStore.cs:8-21` xmldoc calls out cross-process last-write-wins explicitly.** Not debt per se but the first acknowledgment of the race in-source. If either the tray or the widget grows additional state keys, document the partition in the same xmldoc block.

## 6. What remains unchanged from v1 (pointers, not reproduced)

- **Architecture diagram** (v1 §3), **entry-point table** (v1 §4 — `IsComServerMode` row is now accurate), **module map** (v1 §5 — add `Core/Concurrency/{AsyncKeyedLock,AsyncSafe}.cs` + `Core/Cli/IObsidianCliEnvironment.cs` to the Core layer), **reference graph** (v1 §6), **canonical user journey** (v1 §7 — step 5 and step 11 now run under `_gate.WithLockAsync`), **data & state** (v1 §8), **seam table** (v1 §9 — append three rows from §3 of this refresh), **recommended reading order** (v1 §12 — insert `Core/Concurrency/AsyncKeyedLock.cs` + `AsyncSafe.cs` between items 5 and 6), **blind spots** (v1 §13).

## 7. Blind spots specific to this refresh

- I did not re-audit the `Adaptive Cards` JSON templates end-to-end — trusting `audit-reports/card-author.md` for the 1.5 downgrade.
- I did not execute the test suite for this sweep; test existence is confirmed by file listing only.
- I did not re-run `git log`/`git blame` across every touched file — deltas derived from the two landed commits + working-tree diff against v1.
- `tools/New-DevCert.ps1` and `tools/Sign-DevMsix.ps1` bodies were not read line-by-line; the claims about DPAPI / user-ACL are cross-referenced from `CHANGELOG.md:22-23` and `audit-reports/security-auditor.md` rather than primary source.
- `winget/` and `.github/workflows/*` remain uninspected, as in v1.
