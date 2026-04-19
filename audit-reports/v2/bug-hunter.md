# Bug-Hunter Sweep v2 — ObsidianQuickNoteWidget

Read-only follow-up audit. No source modified. Verifies the v1 findings B1–B17 against the post-hardening tree (`CHANGELOG.md` "Unreleased") and surfaces fresh findings introduced by the churn around `FireAndLog` / `AsyncKeyedLock` / CLI stdout parsing / widgetId threading.

Legend — Likelihood: HIGH / MED / LOW. Severity: HIGH (data loss / user-visible failure), MED (degraded UX / hidden failure), LOW (cosmetic / very narrow).

Repo baseline: HEAD @ `d2cb0f6` (security hardening on top of initial commit `f12a196`). Only two commits in history; "Unreleased" CHANGELOG entries correspond to fixes staged on `f12a196`.

---

## 1. Verification of v1 findings (B1–B17)

| ID  | Title                                                            | Status             | Evidence / notes |
|-----|------------------------------------------------------------------|--------------------|------------------|
| B1  | R-M-W race on `WidgetState` (lost updates, in-process)           | **FIXED** (in-proc) | Every `Get → mutate → Save` in `ObsidianWidgetProvider` now runs under `_gate.WithLockAsync(id, …)`: `CreateWidget` L67–75, `DeleteWidget` L86–91, `OnActionInvoked→CreateNoteAsync` L245–306, `OnWidgetContextChanged` L116–124, `Activate` L133–137, `RefreshFolderCacheAsync` L342–350, `RefreshAllActiveAsync` L367–378, and `FireAndLog`'s `onError` L170–180. `AsyncKeyedLock` is a reference-counted per-key `SemaphoreSlim` with correct disposal under `SyncRoot`. **Cross-process LWW between tray and widget remains** — explicitly acknowledged in `JsonStateStore` xmldoc (L15–21). Not regressed, but that residual risk is not a new finding either. |
| B2  | Timer resurrects deleted widget state rows                       | **FIXED**          | `RefreshAllActiveAsync` L372: re-checks `_active.ContainsKey(id)` **under the gate** and skips `_store.Save`. `DeleteWidget` L86–91 removes from `_active` and deletes from store under the same per-id gate, so a concurrent refresh either runs first (save wins; delete cleans up) or observes the removal (save short-circuits). Note: `Deactivate` still bypasses the gate — see **N1**. |
| B3  | `OnActionInvoked` fire-and-forget swallows async exceptions      | **FIXED**          | `FireAndLog` (L160–186) wraps the work in `AsyncSafe.RunAsync`, logs via `ILog`, and on fault runs `onError` which writes `LastError` under the gate and calls `SafePushUpdate`. `AsyncSafe.RunAsync` never throws and also guards the `onError` handler itself (`AsyncSafe.cs:33`). Regression coverage lives in `AsyncSafeTests`. |
| B4  | `JsonStateStore.Persist` swallows `.tmp` / Move failures         | **UNFIXED**        | `JsonStateStore.cs:90–103` — the outer `catch { }` is unchanged. No log call, no telemetry; writes silently vanish on sharing violation. |
| B5  | `FileLog` cross-process sharing violation                        | **UNFIXED**        | `FileLog.cs:72–81` — still `File.AppendAllText` under an in-process `Lock` with `catch { }`. Cross-process log lines still drop silently. Mitigated slightly because log content is now CRLF-sanitized (good F-03 side-effect) but the drop itself is untouched. |
| B6  | `TryReadClipboardText` sync-over-async on COM thread             | **UNFIXED**        | `ObsidianWidgetProvider.cs:464` — `op.AsTask().GetAwaiter().GetResult()` unchanged. Currently called on a threadpool thread via `FireAndLog`, so deadlock is avoided by accident. |
| B7  | `ObsidianCli.RunAsync` truncates stderr on timeout                | **UNFIXED**        | `ObsidianCli.cs:93–97` — on `OperationCanceledException` the result is `(-1, stdout.ToString(), "obsidian CLI timed out", …)`. Real stderr is discarded and the async output/error reader tasks are orphaned. |
| B8  | `RunAsync` rethrows `IOException` from `StandardInput.WriteAsync` | **UNFIXED**        | `ObsidianCli.cs:80–84` — the `await proc.StandardInput.WriteAsync(stdin)` is outside any try/catch, so an early-exit process (stdin closed) escapes `RunAsync` entirely instead of surfacing as a `CliResult`. Still latent — no caller currently passes a non-null stdin. |
| B9  | `DuplicateFilenameResolver` TOCTOU vs. CLI create                | **PARTIALLY FIXED** | `ObsidianCli.CreateNoteAsync` now parses `Created:` / `Overwrote:` and returns the *actual* path (which the CLI silently renames on collision, e.g. `p1.md` → `p1 1.md`), bounding the worst case to "rename" rather than "overwrite". The TOCTOU window between `DuplicateFilenameResolver.ResolveUnique` and `_cli.CreateNoteAsync` still exists (`NoteCreationService.cs:75–77`), but damage is now auto-renamed by the CLI and propagated back to the UI. Residual edge case: two surfaces resolving identical stems at the identical instant still both call `create` with the same path; one gets the rename, one wins. |
| B10 | `QuickNoteForm` folder-fetch Task crashes after dispose          | **UNFIXED**        | `QuickNoteForm.cs:64–73` — still no `IsDisposed` / cancellation check around `BeginInvoke`. |
| B11 | `QuickNoteForm.CreateAsync` is async-void via lambda              | **UNFIXED**        | `QuickNoteForm.cs:53,57` — `_create.Click += async (_, _) => await CreateAsync();` still async-void; the `try/finally` still only toggles `_create.Enabled`. Throws bubble to `Application.ThreadException`, leaving status stuck on `"Creating…"`. |
| B12 | `ParseFolders` silently drops folders starting with `.`          | **UNFIXED** (design) | `ObsidianCliParsers.cs:41` — behavior preserved, no explicit pinning test yet. |
| B13 | `CardTemplates.Load` uses `EndsWith(name)`                       | **UNFIXED**        | `CardTemplates.cs:18` — still `EndsWith(name, StringComparison.Ordinal)`. A future embedded resource named e.g. `Foo.QuickNote.medium.json` would poison `Load("QuickNote.medium.json")`. |
| B14 | `IsReservedWindowsName` misses `CONIN$`, `CONOUT$`               | **UNFIXED**        | `FilenameSanitizer.cs:41–42` — still only CON / PRN / AUX / NUL / COM1–9 / LPT1–9. |
| B15 | `FolderPathValidator` accepts UNC paths                          | **UNFIXED**        | `FolderPathValidator.cs:24` normalizes `\\` → `//`; L27 checks `normalized[1] == ':'` (only drive-letter); L31 `Trim('/')` strips the leading slashes, so `\\server\share\foo` becomes `server/share/foo` which passes segment validation. |
| B16 | `CardDataBuilder` NRE on null entries in persisted lists         | **UNFIXED** (risk reduced) | `CardDataBuilder.cs` still iterates state lists without null-sanitizing. Modern .NET makes `Path.GetFileNameWithoutExtension(null)` return `null` without throwing, and `Add()` short-circuits on `IsNullOrWhiteSpace`, so a wedge via null entries is harder than v1 implied — but `StringComparer.OrdinalIgnoreCase.Equals(null, null)` still counts toward Distinct/Contains semantics, and a hand-edited `state.json` with explicit nulls in `RecentNotes` still surfaces as a literal empty-title entry. |
| B17 | Timer never disposed                                             | **ACCEPTED**       | `ObsidianWidgetProvider.cs:36–39` comment unchanged. COM host tears down the process; no regression. |

### Verification summary

- **Top-3 hardening targets (B1 / B2 / B3) are all FIXED** with in-process correctness. `AsyncKeyedLock` is implemented correctly (ref-counted, disposal under `SyncRoot`, lost-race retry loop — see `AsyncKeyedLock.cs:73–96`); I could not construct a self-deadlock, a double-dispose, or a post-dispose `WaitAsync` scenario by close reading. `PerWidgetGateTests` lives in the test project as evidence.
- The remaining UNFIXED items are all LOW-to-MED individually; none are regressed by the current round of fixes.

---

## 2. New findings (post-B3 churn)

Focus areas: CLI stdout parsing, `AsyncSafe`/`FireAndLog` wrapper re-entrancy, per-widget gate disposal/resurrection, widgetId threading into data blocks, and the widget-provider ↔ `_active` coupling.

### N1. `Deactivate` mutates `_active` without the gate — lets an in-flight action save to a deactivated widget
**File:** `Providers/ObsidianWidgetProvider.cs:142`
```
public void Deactivate(string widgetId) { _log.Info(...); _active.TryRemove(widgetId, out _); }
```
Every other `_active`-mutating entrypoint acquires `_gate` first (`CreateWidget`, `DeleteWidget`, `Activate`). `Deactivate` does not. A `CreateNoteAsync` task that is already past its `!_active.ContainsKey(session.Id) return;` gate-check will happily proceed to `_store.Save(state)` after deactivation. `PushUpdate` then early-returns because `_active` no longer has the id, but the state was persisted anyway. On reactivation, the user sees a possibly-stale "Created …" status they never observed.
**Likelihood:** LOW (user has to deactivate during a CLI round-trip). **Severity:** LOW (stale status string, no data loss).

### N2. `OnActionInvoked` re-adds to `_active` **outside** the gate — resurrects a deleted widget
**File:** `Providers/ObsidianWidgetProvider.cs:102–106`
```
var session = _active.GetOrAdd(id, _ => new WidgetSession(id, definitionId, _store.Get(id).Size));
await HandleVerbAsync(...).ConfigureAwait(false);
```
If an action arrives for an id that has just been through `DeleteWidget` (delete flushed `_active` and the store entry), `GetOrAdd` re-populates `_active` before `HandleVerbAsync` runs. Inside `CreateNoteAsync`, the guard `!_active.ContainsKey(session.Id) return;` is satisfied (we just put it back), so the code reads a default `WidgetState` from the store and writes it back — **recreating the deleted row in `state.json`** with default `Size = "medium"`. This is the exact failure mode B2 guarded against, just through a different door.
**Likelihood:** LOW (requires COM host to dispatch an action to a deleted widget — not impossible given delete-while-pending-action sequencing). **Severity:** MED (orphan row in `state.json`).
**Suggested fix shape:** `_active.GetOrAdd` should happen **under** `_gate.WithLockAsync(id, …)` alongside a "was this widget deleted?" tombstone, or the create guard should consult a `HashSet<string> _deleted` marker written by `DeleteWidget`.

### N3. `CreateNoteAsync` re-reads state **outside** the gate to decide whether to refresh folders
**File:** `Providers/ObsidianWidgetProvider.cs:312–316`
```
var latest = _store.Get(session.Id);
if (string.IsNullOrEmpty(latest.LastError))
    _ = FireAndLog(() => RefreshFolderCacheAsync(session.Id), session.Id, "refreshFolderCache");
```
This read is explicitly called out as "best-effort" in the comment above, but it can see a concurrent writer's in-flight error (or lack thereof) and mis-decide. On a deleted widget, `_store.Get` returns a default `WidgetState` whose `LastError` is empty → the refresh path fires for a deleted id (harmless inside `RefreshFolderCacheAsync` because it re-checks under the gate, but still a wasted CLI round-trip on every delete-after-create).
**Likelihood:** LOW. **Severity:** LOW.
**Fix shape:** capture `result.Status` from inside the gated block into a local, and branch on that instead of re-reading.

### N4. `FireAndLog.onError` silently drops the user-visible error when `_active` no longer has the id
**File:** `Providers/ObsidianWidgetProvider.cs:170–181`
```
await _gate.WithLockAsync(widgetId, () => {
    if (_active.ContainsKey(widgetId)) { var s = _store.Get(widgetId); s.LastError = ex.Message; _store.Save(s); }
    return Task.CompletedTask;
});
SafePushUpdate(widgetId);
```
If `Deactivate` or `DeleteWidget` sneaks in between the throw and the onError acquisition, the `LastError` write is suppressed (correct for delete; arguably wrong for deactivate — the error never makes it to disk, so the user never sees it on next activation). B3's fix is structurally complete, but this edge is not covered.
**Likelihood:** LOW. **Severity:** LOW-MED. (Combines with N1.)

### N5. `FireAndLog` double-pushes updates on failure when `pushUpdateOnCompletion=true`
**File:** `Providers/ObsidianWidgetProvider.cs:160–186`
```
return AsyncSafe.RunAsync(work, _log, ..., onError: async ex => { ...; SafePushUpdate(widgetId); })
    .ContinueWith(_ => { if (pushUpdateOnCompletion) SafePushUpdate(widgetId); }, TaskScheduler.Default);
```
On failure, `onError` calls `SafePushUpdate`, then the `ContinueWith` also does — `WidgetManager.UpdateWidget` is invoked twice per failed action. Not incorrect (idempotent), but doubles host RPC traffic and is surprising.
**Likelihood:** HIGH (every failed action with `pushUpdateOnCompletion: true` — i.e. all action verbs, contextChanged, activate, createWidget). **Severity:** LOW (wasted IPC; no data issue).

### N6. `BuildCliMissingData` is called without `widgetId` — CLI-missing card's action data block has empty `widgetId`
**File:** `Providers/ObsidianWidgetProvider.cs:395`
```
data = CardDataBuilder.BuildCliMissingData("`obsidian` was not found on PATH.");
```
But the signature is `BuildCliMissingData(string? detail = null, string widgetId = "")` and the CliMissing template binds `"data": { "widgetId": "${widgetId}", "verb": "recheckCli" }`. Since the data object has no `widgetId` key populated, the action payload has `widgetId = ""`. The CHANGELOG line *"widgetId threaded into every action data block (card-author)"* is therefore **incompletely implemented for the CliMissing path**. `OnActionInvoked` routes on `actionInvokedArgs.WidgetContext.Id` (not the payload), so the actual `recheckCli` action still works — but any future consumer that trusts `data.widgetId` (e.g. a server-side action relay) would receive empty strings from CliMissing cards only.
**Likelihood:** HIGH (happens every PushUpdate when CLI is unavailable). **Severity:** LOW today, MED if anyone starts relying on data.widgetId.
**Fix shape:** `CardDataBuilder.BuildCliMissingData("…not found on PATH.", widgetId)`.

### N7. Cross-template inconsistency: `${widgetId}` vs `${$root.widgetId}`
**Files:** `AdaptiveCards/Templates/RecentNotes.json:24` uses `"${$root.widgetId}"`, while `QuickNote.small/medium/large.json` and `CliMissing.json` use bare `"${widgetId}"` in action data. Both resolve correctly today — bare `${widgetId}` works in the top-level `actions` array because the current data context is root. But any future template that moves an action inside a `$data`-bound block (as RecentNotes has, for the `openRecent` selectAction) will silently bind the wrong scope and emit the item field instead of the widget id.
**Likelihood:** LOW today, MED on future template edits. **Severity:** LOW (wrong widget id in payload, but again `OnActionInvoked` uses `WidgetContext.Id`).
**Fix shape:** standardize on `${$root.widgetId}` everywhere.

### N8. `QuickNoteForm.CreateAsync` unconditionally hides the form 600ms after success — disrupts a user who re-opened during the delay
**File:** `ObsidianQuickNoteTray/QuickNoteForm.cs:125–130`
```
if (r.Status == NoteCreationStatus.Created || r.Status == NoteCreationStatus.AppendedToDaily) {
    _title.Clear(); _body.Clear();
    await Task.Delay(600);
    Hide();
}
```
Sequence: user submits → form saves → starts 600 ms delay. User presses Ctrl+Alt+N within 600 ms → `Focus()` re-shows the form and starts typing. The pending `await Task.Delay(600)` resumes → `Hide()` is called → **form disappears mid-typing**. Ctrl+Enter submit *also* suffers — every successful create re-clears and re-hides, even if another create was queued.
**Likelihood:** MED (any rapid user). **Severity:** MED (disruption + lost in-progress input).
**Fix shape:** capture a `_submissionToken = new object()` local before the delay; after the delay, only Hide if `_submissionToken` is still current.

### N9. `ObsidianCli.RunAsync` on timeout: orphaned reader tasks and hard-coded stderr
(See B7, re-listed under new-findings so the reader tasks dimension is explicit.) `BeginOutputReadLine` / `BeginErrorReadLine` are never awaited; on `OperationCanceledException` we `Kill` and return. Any stderr output buffered by the reader thread after `Kill` lands in a `StringBuilder` we never read again.
**Likelihood:** LOW. **Severity:** LOW (debuggability).

### N10. `CliResult.Succeeded => ExitCode == 0` contradicts the CLI's actual semantics
**File:** `Cli/CliResult.cs:5` + `Cli/ObsidianCli.cs:148` comment `"The CLI returns exit=0 for every error; authoritative signal is stdout."` Every mutating call site correctly gates on `!r.Succeeded || HasCliError(r.StdOut)` — but a future contributor reading `Succeeded` is very likely to trust it. This is a landmine, not a bug today.
**Likelihood:** MED (time-to-regression). **Severity:** MED when it triggers.
**Fix shape:** rename to `ProcessExitedCleanly` or remove the property and force every caller to inspect stdout.

### N11. `RefreshAllActiveAsync` awaits per-id `FireAndLog` serially inside the loop
**File:** `Providers/ObsidianWidgetProvider.cs:365–379`
```
foreach (var id in _active.Keys.ToArray())
    await FireAndLog(() => _gate.WithLockAsync(id, ...), id, "refreshAllActive", ...).ConfigureAwait(false);
```
Because each per-id save blocks the loop, a single slow `PushUpdate` / `UpdateWidget` RPC stalls updates for every other widget for that tick. Not a correctness bug — each iteration's gate holds briefly — but the loop could/should be `Task.WhenAll` of the per-id tasks.
**Likelihood:** LOW (few widgets, host RPC is fast). **Severity:** LOW.

### N12. **`CreateNoteAsync` persists unvalidated user input into `state.LastFolder` before checking the result** — bad folders become sticky
**File:** `Providers/ObsidianWidgetProvider.cs:277–303`
```
var result = await _notes.CreateAsync(req).ConfigureAwait(false);  // may return InvalidFolder

state.LastFolder = folder ?? string.Empty;          // <-- written unconditionally
state.AutoDatePrefix = autoDate;
...
if (result.Status == NoteCreationStatus.Created ...) { ... }
else if (result.Status == NoteCreationStatus.AppendedToDaily) { ... }
else { state.LastStatus = null; state.LastError = result.Message; }

_store.Save(state);
```
If the user typed a folder that fails `FolderPathValidator` (e.g. `../escape`, `C:\abs`, empty segment, reserved name), `NoteCreationService.CreateAsync` returns `NoteCreationStatus.InvalidFolder` with a descriptive message, **but the provider has already overwritten `state.LastFolder` with the rejected input**. `Save` persists it. The next card render seeds `inputs.folder` from `state.LastFolder`, offering the bad value as the default; the next create attempt fails identically. The user is now "stuck" unless they manually clear the field.
**Likelihood:** MED (the folder combobox explicitly advertises "type new or pick", inviting free-form typing). **Severity:** MED (persistent error state; worse, the bad folder is also serialized into `state.json` where an operator inspecting logs sees what looks like a legitimate cached folder).
**Fix shape:** only assign `state.LastFolder = folder ?? string.Empty;` inside the `result.Status == Created` branch, mirroring how `state.LastCreatedPath` is already gated.

---

## 3. Top-3 summary

1. **N12 — `state.LastFolder` is persisted from unvalidated user input before the result check.** MED × MED. Persistent bad-folder re-offer cycle; also pollutes `state.json` with rejected paths. Simple fix (move the assignment inside the success branch).
2. **N8 — Tray form's 600 ms delayed `Hide()` race.** MED × MED. Any user fast enough to re-open within the delay has their next session hidden mid-typing. Fix via a submission-epoch token.
3. **N2 / N6 — Gate gaps around `_active`:** `OnActionInvoked.GetOrAdd` bypasses the gate (resurrection of deleted ids — N2), and `BuildCliMissingData` isn't passed a `widgetId` (violates the card-author invariant — N6). Neither is user-visible today, but both erode the exact guarantees the recent hardening round was supposed to establish.

All other new findings are LOW × LOW and listed for completeness. **No regressions** were introduced by the B1/B2/B3 fixes — the `AsyncKeyedLock` / `AsyncSafe` / `FireAndLog` trio is structurally correct.
