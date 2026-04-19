# Bug-Hunter Sweep — ObsidianQuickNoteWidget

Read-only latent-bug audit. No source modified.

Scope: `src/ObsidianQuickNoteWidget/**`, `src/ObsidianQuickNoteWidget.Core/**`, `src/ObsidianQuickNoteTray/**`.
Method: manual review focused on concurrency, lifetime, error-swallowing, TOCTOU, parser edges. No new tests executed (read-only).

Legend — Likelihood: HIGH / MED / LOW. Severity: HIGH (data loss / user-visible failure), MED (degraded UX / hidden failure), LOW (cosmetic / very narrow).

---

## B1. Read-modify-write race on `WidgetState` — lost updates

**File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs`
**Lines:** 155–223 (`CreateNoteAsync`), 241–254 (`RefreshFolderCacheAsync`), 261–283 (`RefreshAllActiveAsync`), 285–291 (`WriteStatus`), 92–102 (`OnWidgetContextChanged`)
**Also:** `src/ObsidianQuickNoteWidget.Core/State/JsonStateStore.cs` lines 32–51.

**Description.** Every call site follows the pattern
```
var state = _store.Get(id);   // returns a *clone* of cached state
... mutate state ...
_store.Save(state);           // full replace into cache + persist whole dict
```
`JsonStateStore.Get` returns a fresh clone (line 37, `Clone(s)`), and `Save` does a last-write-wins replace of the entire `WidgetState` object (line 48). The `Lock _gate` only serializes individual `Get`/`Save` calls; it does **not** turn the read-modify-write sequence atomic.

Concretely, any two of the following running concurrently for the same `widgetId` will clobber each other:
- `OnActionInvoked` → `CreateNoteAsync` (updates `LastFolder`, `RecentNotes`, `TagsCsv`, `Template`, `LastStatus`).
- Periodic `RefreshAllActiveAsync` via the timer (updates `CachedFolders`, `CachedFoldersAt`).
- User-triggered `RefreshFolderCacheAsync` from `CreateWidget` / `Activate` / after create (same fields).
- `OnWidgetContextChanged` (updates `Size`).
- `WriteStatus` from the error path of `OnActionInvoked` (updates `LastStatus`, `LastError`).

E.g. user submits `createNote`. Mid-flight the 2-minute timer fires `RefreshAllActiveAsync`, which snapshots state **before** `CreateNoteAsync` finishes, writes `CachedFolders` back, and erases the `LastFolder` / `RecentNotes` / `LastStatus` that `CreateNoteAsync` just wrote (or vice versa). One update silently vanishes.

**Cross-process multiplier.** `Program.cs` in both the Widget COM server **and** the Tray app each instantiate their own `JsonStateStore` over the same file. The in-process `Lock` provides zero protection across processes — any tray save while the widget host has a populated `_cache` is overwritten on the widget's next `Persist()` (and vice versa). The tray uses a fixed key `"tray"` (QuickNoteForm.cs:14), so collisions between tray and widget entries in the shared dict are the norm, not the exception.

**Repro hypothesis.**
1. Pin a widget; let it call `RefreshFolderCacheAsync` (updates `CachedFolders`).
2. Invoke `createNote` on that widget. While `NoteCreationService.CreateAsync` is awaiting the CLI (~hundreds of ms), fire the timer (or trigger a second refresh via `Activate`).
3. Observe `state.json` afterward — exactly one of `{CachedFolders changes}` or `{LastFolder/RecentNotes/LastStatus changes}` persists; the other is dropped.

**Likelihood:** HIGH (the 2-minute timer + every note creation taking real CLI time guarantees overlapping R-M-W windows).
**Severity:** HIGH (silent user-visible data loss: recents missing, LastFolder resetting, folder cache not updating).

**Proposed regression test.** In `JsonStateStoreTests`, spin up N (say 8) tasks that each `Get`, mutate a distinct field, then `Save` in a loop for 500 iterations against a shared store. Assert that after all tasks finish, the final state reflects the last per-field write from *each* task (i.e. a merge). Current code will fail — whichever task saved last wins the whole record. Also: a second test with two `JsonStateStore` instances pointing at the same temp path, interleaved writes under different widgetIds — assert neither side's entry is lost.

---

## B2. Timer resurrects deleted widgets — `DeleteWidget` vs `RefreshAllActiveAsync`

**File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs`
**Lines:** 65–70 (`DeleteWidget`), 261–283 (`RefreshAllActiveAsync`).

**Description.** `DeleteWidget` does:
```
_active.TryRemove(widgetId, out _);
_store.Delete(widgetId);
```
`RefreshAllActiveAsync` does:
```
foreach (var id in _active.Keys.ToArray()) {
    var state = _store.Get(id);     // returns a blank state if absent
    state.CachedFolders = folders;
    _store.Save(state);              // re-creates the deleted entry
    PushUpdate(id);
}
```
Ordering race: if `DeleteWidget` runs **between** the `_active.Keys.ToArray()` snapshot and the per-id `_store.Save(state)`, the just-deleted widget's state is re-created in `state.json` with `WidgetId = id` and everything else default. `PushUpdate` is then called for a deleted widget — `_active.TryGetValue` returns false there, so the push is skipped, but the ghost record is now leaking in `state.json` forever (nothing cleans up stale ids on startup). Even absent the race, `Deactivate` (line 113) removes from `_active` but never from `_store`, so stale state accumulation is already a latent leak; the timer just accelerates it.

A secondary variant: `Deactivate` happens, timer fires after — `_active` is empty so the iteration is a no-op. But `Activate` → `DeleteWidget` with refresh in flight still reproduces B2.

**Repro hypothesis.** Call `CreateWidget("w1")`; manually trigger the timer tick (or wait 2 min) but block in the middle of the foreach by stubbing `_store.Save` to delay; from another thread call `DeleteWidget("w1")`; let save proceed. Read `state.json` — `w1` is back, with default `Size = "medium"` and blank settings.

**Likelihood:** MED (narrow window, but widened by CLI latency of `ListFoldersAsync` — dozens of ms inside the foreach body isn't required since Get/Save themselves take the lock serially; the window is after delete and before that widget's turn in the loop).
**Severity:** MED (orphaned state rows in `state.json`; if user re-pins with a reused id, stale defaults surface). Data isn't lost but file grows unbounded.

**Proposed regression test.** `ObsidianWidgetProviderTests` (new): fake `IStateStore` that counts calls and a fake `IObsidianCli` whose `ListFoldersAsync` awaits a `TaskCompletionSource` the test controls. Start `RefreshAllActiveAsync`, call `DeleteWidget` while it's suspended, complete the TCS, then assert the fake store received no `Save` for the deleted id (and no `PushUpdate` for it). Requires `_active` snapshot to be re-checked per iteration, or `Delete` to tombstone `_active`.

---

## B3. `OnActionInvoked` fire-and-forget swallows async exceptions

**File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs`
**Lines:** 72–90.

**Description.**
```
try {
    var session = _active.GetOrAdd(...);
    _ = HandleVerbAsync(session, verb, data);   // fire-and-forget Task
} catch (Exception ex) {
    _log.Error(...); WriteStatus(...); PushUpdate(...);
}
```
The `catch` only sees **synchronous** faults from `GetOrAdd`/`_store.Get`. Anything thrown (or faulted) inside `HandleVerbAsync` — which is the overwhelming majority of the work (note creation, CLI calls, clipboard read, JSON parse) — escapes into the unobserved-task handler. The user sees no status update, no error banner; the widget UI stays stale. There is no top-level `try` inside `HandleVerbAsync` around the `switch`, and `CreateNoteAsync` / `HandleOpenRecentAsync` have narrow catches only around JSON parse. If `_cli.CreateNoteAsync` throws (not returns null — e.g. NRE, unexpected exception from `Process.Start`), or if `_store.Save` throws post-CLI, the error is completely invisible to the user.

Bonus: `PushUpdate(session.Id)` at the end of `HandleVerbAsync` (line 152) is outside any try; so a throw earlier means no post-action UI refresh either.

**Repro hypothesis.** Substitute `IObsidianCli` stub whose `CreateNoteAsync` throws `InvalidOperationException("boom")`. Fire `OnActionInvoked` with verb `createNote`. Assert that `WriteStatus(_, null, "boom")` was called and `PushUpdate` was issued — today, neither happens.

**Likelihood:** MED (most CLI errors are caught-and-logged, but uncaught throws do happen in production — Process start races, disposed handles, out-of-memory during JSON serialization of large states).
**Severity:** HIGH when it triggers (user pressed "Create", widget pretends nothing happened, no "Creating…"/error transition).

**Proposed regression test.** As above — provider test with a throwing `IObsidianCli` stub, assert `LastError` in the fake store becomes "boom" and `PushUpdate` was called exactly once after the failure. Fix shape: wrap `HandleVerbAsync` body in a try/catch and route to `WriteStatus`+`PushUpdate`, OR make `OnActionInvoked` attach a `.ContinueWith(TaskContinuationOptions.OnlyOnFaulted)` that does the same.

---

## Other findings (lower-priority, listed for completeness)

### B4. `JsonStateStore.Persist` leaks `.tmp` and silently drops writes
`src/ObsidianQuickNoteWidget.Core/State/JsonStateStore.cs:76–89`. On `File.Move` failure (sharing violation across processes — see B1 cross-process), the outer `catch { }` swallows the exception. The `_cache` remains updated in RAM but disk is stale, and `.tmp` is left behind. Next `Persist` overwrites `.tmp` so the leak is bounded at one file, but user edits that happened to race with another process's move are lost without any log entry. **Likelihood MED, Severity MED.** Test: open `_path` with `FileShare.None` from another handle, call `Save`, assert either an exception surfaces *or* a log warning is emitted. Today: neither.

### B5. `FileLog.Roll` + `File.AppendAllText` cross-process sharing violation
`src/ObsidianQuickNoteWidget.Core/Logging/FileLog.cs:28–56`. Tray and widget processes both write `log.txt`. `AppendAllText` opens with default share mode; concurrent writes throw → swallowed in the catch. Log lines are silently dropped when both processes log at the same moment. **Likelihood LOW-MED, Severity LOW** (logging is diagnostic, not functional — but makes debugging B1/B2/B3 harder). Test: two threads/processes hammering `Info` for 1s; assert line count ≈ 2 × calls with tolerance. Today will fail fuzzily.

### B6. `TryReadClipboardText` synchronous-over-async on the COM thread
`src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:366–380`. `op.AsTask().GetAwaiter().GetResult()` inside an STA COM-reachable thread is a classic deadlock vector if the clipboard WinRT op needs to marshal back to the same apartment. Today the surrounding call chain is itself inside a fire-and-forget `HandleVerbAsync` so it's running on a threadpool thread — deadlock is currently avoided by accident. If anyone refactors `HandleVerbAsync` to sync, it will hang. **Likelihood LOW, Severity HIGH-if-triggered.** Test: call `OnActionInvoked(verb="pasteClipboard")` with a synchronization context that throws on block; assert it completes. Prefer `await op.AsTask()` in a real async method.

### B7. `ObsidianCli.RunAsync` — stderr truncation on timeout
`src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs:83–91`. On `OperationCanceledException`, `proc.Kill` fires but the async stdout/stderr readers are not awaited (`WaitForExitAsync` was what we cancelled; the reader tasks from `BeginOutputReadLine` are orphaned). The returned `CliResult` surfaces `stdout.ToString()` but only whatever was captured so far, and `"obsidian CLI timed out"` for stderr — actual stderr is discarded. Debugging a hung CLI becomes impossible. **Likelihood LOW, Severity LOW.** Test: fake a process that prints to stderr then hangs; assert stderr text is preserved in the result.

### B8. `ObsidianCli.RunAsync` — unhandled throw from `StandardInput.WriteAsync`
`ObsidianCli.cs:74–78`. If the target process exits before we write stdin (not uncommon for `--version`-style probes), `WriteAsync` throws `IOException`. That escapes `RunAsync` entirely — callers (`NoteCreationService`, `RefreshFolderCacheAsync`) have their own try/catch, so the symptom is "Refresh folder cache failed" logged, not a crash. But the path was supposed to return a `CliResult` with error info; today it throws through. **Likelihood LOW** (stdin path is currently unused by any caller — all callers pass `stdin: null`), Severity LOW. Test: pass a `stdin` argument to a process that exits immediately; assert a `CliResult(exitCode=…, StdErr=exception message)`, not a thrown exception.

### B9. `DuplicateFilenameResolver` TOCTOU vs. file creation
`src/ObsidianQuickNoteWidget.Core/Notes/DuplicateFilenameResolver.cs:11–31`. Probe `exists(candidate)` is decoupled from `_cli.CreateNoteAsync` by the CLI round-trip latency. If two widgets (or widget + tray) both resolve the same stem at the same instant, they both land on `{stem}.md` and one CLI call overwrites the other's note or fails. Obsidian CLI's own overwrite semantics determine the damage. **Likelihood LOW** (requires same title+folder within ~seconds from two surfaces), **Severity MED** (data loss of note content). Test: parallel `NoteCreationService.CreateAsync` with identical requests against a fake CLI tracking target paths — assert distinct paths. Today: can collide.

### B10. `QuickNoteForm` constructor's folder-fetch task crashes after dispose
`src/ObsidianQuickNoteTray/QuickNoteForm.cs:64–73`. `Task.Run(async () => { … BeginInvoke(…); })` holds no cancellation and no dispose check. If `Program.cs`'s `using` disposes the form before `ListFoldersAsync` returns (e.g. fast Ctrl+C after launch), `BeginInvoke` throws `ObjectDisposedException` on the threadpool thread → `UnobservedTaskException`. **Likelihood LOW, Severity LOW.** Test: dispose the form immediately after construction, let the task complete; assert no unobserved exception.

### B11. `QuickNoteForm.CreateAsync` is invoked async-void via lambda
`QuickNoteForm.cs:53,57`. `async (_, _) => await CreateAsync()` is effectively async-void; the try/finally inside only guards `_create.Enabled`. If `_notes.CreateAsync` throws (not returns a failure status — e.g. NRE), the exception bubbles to `Application.ThreadException` and the status label stays on "Creating…". **Likelihood LOW-MED, Severity MED.** Test: throwing fake service, click button, assert `_status.Text` shows error (not "Creating…"). Today fails.

### B12. `ObsidianCliParsers.ParseFolders` silently drops any folder starting with `.`
`src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCliParsers.cs:41`. Design choice, but `.config`, `.notes`, `.archive` — legitimate user folders — vanish from the dropdown. Not a bug per se, documented. **Likelihood as-bug LOW, Severity LOW.** Worth a test pinning the behavior so it's not accidentally loosened/tightened.

### B13. `CardTemplates.Load` uses `EndsWith(name)` matching
`src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardTemplates.cs:17–19`. If a future resource named `FooRecentNotes.json` is embedded, `Load("RecentNotes.json")` returns the wrong one. **Likelihood LOW, Severity LOW.** Test: add a second resource with colliding suffix; assert precise match wins (would need a `.` prefix or full name match).

### B14. `FilenameSanitizer.IsReservedWindowsName` misses `CONIN$`, `CONOUT$`
`src/ObsidianQuickNoteWidget.Core/Notes/FilenameSanitizer.cs:35–43`. Windows also reserves `CONIN$` and `CONOUT$`. User creating a note titled "conin$" will hit a Windows-level create failure via CLI. **Likelihood very LOW, Severity LOW.**

### B15. `FolderPathValidator` absolute-path check is too permissive
`src/ObsidianQuickNoteWidget.Core/Notes/FolderPathValidator.cs:27–28`. Only rejects `X:` at index 1. Does not reject UNC paths (`//server/share`) because `Trim('/')` strips the leading slashes before segment-walking; a malicious `//server/share/foo` becomes `server/share/foo`, which passes segment validation (no illegal chars). It's vault-relative anyway, so Obsidian CLI rejects it downstream, but the validator's intent is defense in depth. **Likelihood LOW, Severity LOW.** Test: input `//evil/share/x`; assert rejected.

### B16. `CardDataBuilder` NRE if persisted lists contain null entries
`src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs:70–80,88`. `PinnedFolders.Distinct(...)`, `.Contains(...)` with null string blows up. `WidgetState` JSON ingestion does not sanitize list contents; a hand-edited `state.json` with `["a", null, "b"]` crashes `PushUpdate` → caught by line 328 broad catch, so widget goes blank with no recovery. **Likelihood LOW** (no writer produces nulls), **Severity MED** (wedged widget). Test: inject null into `state.RecentFolders`, call `BuildQuickNoteData`; today throws.

### B17. Timer is never disposed
`src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:30, 42`. Already documented in the comment ("tied to COM host process"). Accepted; noting for completeness. No finalizer either, so if the COM host does a graceful provider tear-down without process exit, the timer keeps firing and touching a disposed store. Widget host in practice tears down the whole process, so LOW/LOW.

---

## Top 3 by likelihood × severity

1. **B1 — Read-modify-write race on `WidgetState`** (HIGH × HIGH). Every widget action, every 2-minute timer tick, and the tray companion can silently erase each other's writes to `state.json`. This is the single most consequential latent bug.
2. **B3 — `OnActionInvoked` swallows async exceptions from `HandleVerbAsync`** (MED × HIGH). User-facing failures (clicked "Create" → nothing visible happens) are invisible because the fire-and-forget Task has no fault handler.
3. **B2 — Timer resurrects deleted widget state rows** (MED × MED). `RefreshAllActiveAsync` races `DeleteWidget`, re-creating default entries in `state.json` and slowly leaking rows.

All three are rooted in the same architectural gap: no cross-cutting "unit of work" or per-widget serialization between the timer callback, the COM entrypoints, and the persistence store.
