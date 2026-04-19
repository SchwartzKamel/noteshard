# Code Archaeologist — v3 Refresh

> **Archetype:** `code-archaeologist` (read-only, one-shot handoff).
> **Target:** `C:\Users\lafia\csharp\obsidian_widget` at `HEAD = 750f032` (post-dropdown-revert).
> **Delta scope:** refresh of `audit-reports/v2/code-archaeologist.md`. Only material that changed since v2 (or that a new contributor *today* needs above the v2 brief) is reproduced in full. Unchanged sections are pointers.
> **Read order for a new contributor TODAY:** v1 §3–§7 (architecture, entry points, module map, canonical journey) → v2 §3 (new seams) → this v3 (latest deltas + sharp edges) → `README.md` → `CHANGELOG.md`.

---

## 0. TL;DR for a new contributor (read this first)

You're looking at a **.NET 10, Windows-only, MSIX-packaged out-of-proc COM server** that registers as a Windows 11 Widget Board provider, plus a small WinForms tray companion. All Obsidian I/O goes through the official `obsidian` CLI (1.12+).

Three things to internalize before touching code:

1. **The widget host is Windows. The Core lib is portable.** All tests live in `tests/ObsidianQuickNoteWidget.Core.Tests/` and target `Core` only — there is no UI/COM harness. If your change can be expressed in `Core/`, do it there or you lose test coverage.
2. **State is per-widget-id, gated.** Every read-modify-write on `WidgetState` runs under `_gate.WithLockAsync(id, …)` (`AsyncKeyedLock<string>`). Cross-process atomicity (widget host ↔ tray) is *not* provided — partition by id (the tray uses the literal key `"tray"`).
3. **Never throw out of a COM entry point.** Every inbound callback funnels through `FireAndLog` → `AsyncSafe.RunAsync`, which logs, writes `LastError` under the gate, and re-pushes the card. A bare `throw` inside a verb handler will crash the COM host.

If the widget board doesn't render after `make package-install`, jump straight to v1 Gotcha 4 (uninstall-dance) and §4 of this brief (manifest version is now `1.0.0.2`).

---

## 1. The 3 most recent commits (newest first)

| SHA | One-line | Why it landed |
| --- | --- | --- |
| `750f032` | Restore compact folder dropdown + add separate "new folder" text input | **User-feedback revert.** v2 had switched the medium/large folder picker from `style: compact` to `expanded` (a radio list) so the "type new or pick" affordance was discoverable. Users preferred the dropdown back. Compromise: ChoiceSet is `compact` again (`QuickNote.medium.json:18`, `QuickNote.large.json:20`), and a *separate* `Input.Text id="folderNew"` was added directly below it (`medium.json:30-34`, `large.json:31-37`). The provider now prefers the typed value when non-empty (`Providers/ObsidianWidgetProvider.cs:251-254`). Manifest bumped `1.0.0.1 → 1.0.0.2` (`Package.appxmanifest:14`). `CardTemplatesTests.MediumOnlyMarker` updated to the new placeholder text. |
| `d2cb0f6` | Security hardening F-01, F-02, F-03 | Per-developer dev-cert password (`tools/New-DevCert.ps1`, `tools/Sign-DevMsix.ps1`), `obsidian` resolution preference ladder rejecting `.cmd`/`.bat` from PATH, and `FileLog.SanitizeForLogLine` against CRLF/C0 injection. See v2 §3 for the seam contracts these introduced — none of those contracts changed in `750f032`. |
| `f12a196` | Initial commit: widget board provider + audit-driven fixes | Bug-hunter B1/B2/B3 (per-widget gate, no-resurrect, `AsyncSafe`), widget-plumber `IsComServerMode` fix, Adaptive Cards 1.5 + `widgetId` threading, `<AdditionalTasks/>` + manifest `1.0.0.1`, `WidgetsDefinition.xml` deletion. See v2 §2 table for full mapping. |

The repo has **only three commits**. There is no other history, no tags, no branches besides `main`. Don't go hunting for prior context — `git log` lies about a long history that doesn't exist.

---

## 2. What changed in the surface area since v2

### 2.1 Folder input shape (the dropdown revert)

Before `750f032` (v2 state): a single `Input.ChoiceSet style="expanded"` whose options included one synthetic "type new" entry. Single input field bound to `folder`.

After `750f032` (current): **two** form controls per card size (medium + large):

- `Input.ChoiceSet id="folder" style="compact"` — the dropdown picker, sourced from `${$root.folderChoices}` (built by `Core/AdaptiveCards/CardDataBuilder.cs:58-86` from pinned + recent + cached folders).
- `Input.Text id="folderNew"` — free-text override placed directly below.

The `small` template is unchanged — it has no folder input at all (`Templates/QuickNote.small.json`).

Submission logic (`ObsidianWidgetProvider.cs:251-254`):

```csharp
var folderNew = inputs.GetValueOrDefault("folderNew")?.Trim();
var folder = !string.IsNullOrEmpty(folderNew)
    ? folderNew
    : inputs.GetValueOrDefault("folder") ?? state.LastFolder;
```

`folderNew` wins when non-empty after `Trim()`. Whitespace-only input falls through to the picker. The typed folder string then flows into `RememberRecent(state.RecentFolders, folder!, …)` at `:292`, so a one-off typed folder *will* show up in the dropdown on the next render.

### 2.2 `CardDataBuilder` was **not** updated for `folderNew` — latent UX inconsistency

`Core/AdaptiveCards/CardDataBuilder.cs:22-32` still seeds only the original input keys. The two card templates reference `${$root.inputs.folderNew}` as a default value, but no such key is bound. Practical effect:

- Adaptive Cards data binding leaves the field empty when the key is missing → the visible result (empty `folderNew` after each render) is what the UX wants. Net behavior is correct.
- But the template now references an unbound variable. If a future contributor adds `["folderNew"] = state.PendingFolderNew` to the data dict, the field will silently start persisting across renders. That may or may not be intentional. Document the choice rather than leave it implicit.

**Recommendation for the next pass:** either drop the `value` binding from `folderNew` in both templates, or seed `["folderNew"] = string.Empty` explicitly in `CardDataBuilder` so the contract is visible in code.

### 2.3 Manifest version bump

`Package.appxmanifest:14` → `Version="1.0.0.2"`. This is the third version stamp in three commits:

| Commit | Version |
| --- | --- |
| `f12a196` | `1.0.0.1` |
| `d2cb0f6` | `1.0.0.1` (no bump for security hardening — debatable) |
| `750f032` | `1.0.0.2` |

Note: the security hardening commit (`d2cb0f6`) did **not** bump the version. If a packaged build of `1.0.0.1` is in the wild, those installs do not advertise that they include F-01/F-02/F-03 fixes. Low risk in practice (no public distribution yet) but worth flagging for `release-engineer`.

### 2.4 Test marker drift

`tests/ObsidianQuickNoteWidget.Core.Tests/CardTemplatesTests.cs:19` — the `MediumOnlyMarker` is now `"…or type new folder (optional)"` (the `folderNew` placeholder). This is the discriminator that proves size→template routing in `CardTemplates.LoadForSize`. If you change that placeholder string in `QuickNote.medium.json`, **also update this constant** or the test will tell you size routing is broken when it isn't. (Same anti-pattern as v1 Gotcha for the other markers.)

---

## 3. Refreshed Sharp Edges register (what bites a new contributor *today*)

This list supersedes v2 §4. Items that haven't changed are condensed; new or revised items are marked **[NEW v3]** / **[CHANGED v3]**.

1. **STA + native Win32 pump is mandatory.** `Program.cs:15, :84-90`. (Unchanged.)
2. **WinRT marshalling, not classic CCW.** `Com/ClassFactory.cs:50`. (Unchanged.)
3. **3-location ID sync.** CLSID in `WidgetIdentifiers.cs:8`, `Package.appxmanifest:47` (`com:Class Id`), `Package.appxmanifest:66` (`CreateInstance ClassId`). Widget definition IDs in `WidgetIdentifiers.cs:11-12` and `Package.appxmanifest:69, :91`.
4. **Manifest uninstall-dance** after every version bump. *You will hit this on `1.0.0.2`.*
5. **CLI surface is positional `key=value`.** `Core/Cli/ObsidianCli.cs:127-201`.
6. **`content=` escaping order matters.** Backslash first, then newlines (`Core/Cli/ObsidianCliParsers.cs`).
7. **`obsidian ls` does not exist** — use `obsidian folders`.
8. **`GetCurrentThreadId` lives in `kernel32.dll`.** `Com/Ole32.cs:56-57`.
9. **P/Invoke mixing on `CoRegisterClassObject`.** `#pragma warning disable SYSLIB1054` until the source-gen marshaller supports IUnknown-marshalled `object`.
10. **`Platforms = x64;x86;arm64`, `TargetPlatformMinVersion = 10.0.22621.0`.** Unchanged.
11. **Never throw out of COM entry points** — funnel through `FireAndLog`/`SafePushUpdate` (`ObsidianWidgetProvider.cs:384-421`).
12. **`TreatWarningsAsErrors=true`** globally (`Directory.Build.props:5`). Any `// TODO`-style warning will fail CI.
13. **Tests cover `Core` only** — no UI/COM harness. Test files (13): `AsyncSafeTests`, `CardDataBuilderTests`, `CardTemplatesTests`, `DuplicateFilenameResolverTests`, `FileLogTests`, `FilenameSanitizerTests`, `FolderPathValidatorTests`, `FrontmatterBuilderTests`, `JsonStateStoreTests`, `NoteCreationServiceTests`, `ObsidianCliParsersTests`, `ObsidianCliResolutionTests`, `PerWidgetGateTests`.
14. **CLI resolution is *not* a plain PATH lookup.** Preserve the preference ladder (`OBSIDIAN_CLI` → `%ProgramFiles%` → `%LocalAppData%\Programs` → `HKCU\…\obsidian\shell\open\command` → PATH `.com`/`.exe` only). `.cmd`/`.bat` are deliberately rejected.
15. **`AsyncKeyedLock` does not cross processes.** Tray + widget host are separate processes; cross-process atomicity is achieved only by id partitioning (tray uses `"tray"`).
16. **`FireAndLog` `onError` writes `LastError` under the gate then pushes a card update.** Don't add a second write in the work lambda's `catch` — let `AsyncSafe` surface it.
17. **Dev-cert password is per-developer.** Run `tools/New-DevCert.ps1` once per machine; `tools/Sign-DevMsix.ps1` reads it back. Don't hard-code in `Makefile`/docs.
18. **Log lines are sanitized at the sink.** Pre-formatting with `\n` will be escaped to `\\n`. Use `_log.Info` with semantic strings, not formatted multi-line blobs.
19. **[NEW v3] Folder input has *two* fields, not one.** Verb handlers must read `folderNew` first, then fall back to `folder` (`ObsidianWidgetProvider.cs:251-254`). Adding any new "create note"-shaped verb (e.g. an `appendToNote`) needs the same precedence rule, or users typing in the new-folder box will be silently ignored.
20. **[NEW v3] `${$root.inputs.folderNew}` in the templates is bound to a key `CardDataBuilder` does not emit.** This is currently harmless (renders empty, which is what we want) but is a footgun if anyone "fixes" it without checking the template. See §2.2.
21. **[NEW v3] Manifest version bumps are inconsistent.** Security commit `d2cb0f6` did not bump; UX commit `750f032` did. There is no documented rule. Ask `release-engineer` before next bump.
22. **[NEW v3] Post-create folder-cache refresh reads state *outside* the gate** (`ObsidianWidgetProvider.cs:315-318`). The comment at `:311-314` calls this out. In single-process operation it's safe; if the tray writes between `await` points the `LastError` check uses stale data. Latent — same as v2 noted, restated because it sits right next to the new `folderNew` code and is easy to break by accident when refactoring.

---

## 4. Tech-debt register — refreshed

### Resolved since v2

- *None* — `750f032` is a UX revert, not a debt-paydown commit. Nothing in the v2 register has been retired.

### Still open (carried forward, condensed)

- `Program.cs:18-28` — proof-of-life log writer to `%UserProfile%\ObsidianWidget-proof.log`. Should be feature-flagged or removed for a Store build.
- `Com/Ole32.cs:12-22` — `#pragma warning disable SYSLIB1054`, blocked on `[LibraryImport]` IUnknown support.
- `Providers/ObsidianWidgetProvider.cs:39, :51-55` — `_folderRefreshTimer` undisposed; tied to COM host lifetime by xmldoc.
- `Core/State/JsonStateStore.cs:105-106` — JSON round-trip clone on every `Get`/`Save`.
- `Core/Logging/FileLog.cs:78-81, :95` — catch-all `catch { }` (deliberate; logger must never throw).
- `src/ObsidianQuickNoteTray/GlobalHotkey.cs:29` + `Program.cs:42-53` — fixed hotkey id `0x9001`, hard-coded `Ctrl+Alt+N`.
- `tools/WidgetCatalogProbe.cs` + `tools/AppExtProbe/` — undocumented beyond `.github/copilot-instructions.md:48`.
- `Core/Cli/ObsidianCli.cs:24-25` — `TODO(F-02 follow-up)` Authenticode signature check, comment-only.
- `Core/Cli/ObsidianCli.cs:28, :247-253, :296` — `s_warnedPathResolution` static one-shot; tests call `ResetPathWarningForTests()`.
- `Providers/ObsidianWidgetProvider.cs:312-316` — post-create `_store.Get` happens outside the gate (see Sharp Edge 22).
- `Providers/ObsidianWidgetProvider.cs:182-185` — two layers of `ContinueWith` per `FireAndLog`. Allocates; measure before optimizing.
- `tools/New-DevCert.ps1` / `tools/Sign-DevMsix.ps1` — DPAPI/user-ACL password storage; verify on future Windows builds.

### New debt accumulated in v3

- **[NEW v3] `folderNew` is bound in templates but not seeded by `CardDataBuilder`.** See §2.2. Either remove the `value` binding or seed the key. Current behavior is correct by accident.
- **[NEW v3] No test exercises the `folderNew`-takes-precedence rule** in `ObsidianWidgetProvider.CreateNoteAsync`. The provider is Windows-only and not in the test project, but the precedence logic is a pure expression that could be refactored into a small `Core` helper (e.g., `FolderInputResolver.Resolve(folderNew, folderPicker, lastFolder)`) and unit-tested. Without that, the rule is a regression target.
- **[NEW v3] `CHANGELOG.md` has not been updated for `750f032`.** The "Unreleased / Fixed" section still says "Folder ChoiceSet switched from `compact` to `expanded` …" (`CHANGELOG.md:18`), which is now false — that line should be replaced with the dropdown-restore + `folderNew` description. `doc-scribe` should pick this up.
- **[NEW v3] `MediumOnlyMarker` test discriminator is once again brittle string-coupling** (`CardTemplatesTests.cs:19`). Unchanged anti-pattern, just restated because it bit us this commit and will bite again.

---

## 5. What remains unchanged from v1/v2 (pointers, not reproduced)

- **Architecture diagram** → v1 §3.
- **Entry-point table** → v1 §4 (with v2's `IsComServerMode` correction; manifest `Version` is now `1.0.0.2`).
- **Module map** → v1 §5 + v2 §3 additions (`AsyncKeyedLock`, `AsyncSafe`, `IObsidianCliEnvironment`).
- **Reference graph** → v1 §6.
- **Canonical user journey** → v1 §7. **Patch in your head:** at the inputs-parsing hop (provider step 5), there are now two folder inputs; the rest of the journey is unchanged.
- **Data & state** → v1 §8.
- **Seam table** → v1 §9 + v2 §3.
- **Recommended reading order** → v1 §12 + v2 §3 insertion. For v3, also read `QuickNote.medium.json` and `QuickNote.large.json` after step 6 — the form contract lives there, not in code.
- **Blind spots** → v1 §13.

---

## 6. Recommended Day-1 path for a new contributor

1. `README.md` — what the thing is and how to install it locally.
2. `CHANGELOG.md` — three entries, but **note §4 above: it lies about the dropdown line**. Trust git log over the Unreleased section until that's fixed.
3. `src/ObsidianQuickNoteWidget/Program.cs` — the COM-server entry point and Win32 pump.
4. `src/ObsidianQuickNoteWidget/Package.appxmanifest` — the contract with the Widget host.
5. `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs` — the only verb router; everything inbound lands here.
6. `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/QuickNote.{small,medium,large}.json` — the form contract.
7. `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs` — what data is bound into those templates.
8. `src/ObsidianQuickNoteWidget.Core/Notes/NoteCreationService.cs` — the Obsidian-side pipeline.
9. `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs` — process invocation + the F-02 resolution ladder.
10. `src/ObsidianQuickNoteWidget.Core/Concurrency/AsyncKeyedLock.cs` + `AsyncSafe.cs` — the concurrency model.
11. `tests/ObsidianQuickNoteWidget.Core.Tests/` — read at least `PerWidgetGateTests`, `ObsidianCliResolutionTests`, and `CardTemplatesTests`.

---

## 7. Blind spots specific to this refresh

- I did not run the test suite — existence and contents inspected, execution skipped.
- I did not re-audit the Adaptive Card JSON beyond the changed regions in medium/large. Recents-card templates were not re-read.
- The provider source under `src/ObsidianQuickNoteWidget/` was inspected only around the changed `CreateNoteAsync` block (`:230-320`) and the gate/`FireAndLog` regions cited from v2; the rest is taken on v2's word.
- `tools/New-DevCert.ps1` and `tools/Sign-DevMsix.ps1` bodies are still un-read line-by-line (same as v2). Claims about DPAPI/user-ACL trace through `audit-reports/v2/security-auditor.md`.
- `winget/`, `.github/workflows/`, and `Makefile` were not re-audited for drift against the new manifest version.
- I did not verify the Adaptive Card data binding actually leaves `folderNew` empty when the key is missing from `inputs` — this is a documented binding behavior, not source-checked here. Worth a manual smoke test.
