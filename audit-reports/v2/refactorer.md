# Refactorer Sweep v2 — Obsidian Quick Note Widget

**Mode:** READ-ONLY. No source files were modified.
**Baseline:** prior report at `audit-reports/refactorer.md` (file was 395 lines at that snapshot).
**Scope:** same as v1 — all `.cs` under `src/`, behavior contract still the tests under `tests/ObsidianQuickNoteWidget.Core.Tests/`.

---

## What changed since v1

The intervening sweep added **seams**, not **extractions**. Concretely:

1. New `AsyncKeyedLock<string> _gate` field + `Core/Concurrency/AsyncKeyedLock.cs` (111 lines, new).
2. New `AsyncSafe.RunAsync` helper + `Core/Concurrency/AsyncSafe.cs` (37 lines, new).
3. New `FireAndLog(work, widgetId, context, pushUpdateOnCompletion)` wrapper on the provider (≈26 lines).
4. New `SafePushUpdate(widgetId)` trampoline (≈5 lines).
5. Every COM entrypoint (`CreateWidget`, `DeleteWidget`, `OnActionInvoked`, `OnWidgetContextChanged`, `Activate`) was re-wired to `FireAndLog(() => _gate.WithLockAsync(id, async () => { … }))` instead of inline try/catch.
6. `HandleVerbAsync`'s `openRecent` arm was Extract-Method'd into `HandleOpenRecentAsync` (Fowler: Extract Method — good).
7. Periodic folder-cache refresher now routed through `FireAndLog(..., pushUpdateOnCompletion: true)` per widget in `RefreshAllActiveAsync`.

None of the v1 "Extract Class" or "Move Method" suggestions were applied.

---

## Re-measurement of `ObsidianWidgetProvider.cs`

| Metric                                        | v1 (prior) | v2 (now) | Δ        |
|-----------------------------------------------|-----------:|---------:|---------:|
| Raw lines                                     | 395        | **436**  | **+41**  |
| Methods on the type                           | 13         | 16       | +3       |
| Distinct responsibilities mixed into the type | 8          | **8**    |  0       |
| Per-entrypoint inline `try/catch`             | 5          | **0**    | −5       |
| Longest method (`CreateNoteAsync`)            | ~70 lines  | ~80 lines| **+10**  |
| 3-arm `result.Status` branch still inline     | yes        | **yes**  |  —       |
| `RememberRecent` still static on provider     | yes        | **yes**  |  —       |
| `ParseInputs` still returns `Dictionary<,>`   | yes        | **yes**  |  —       |

### Cyclomatic complexity (by inspection)

| Method                   | Before | After  | Notes |
|--------------------------|-------:|-------:|-------|
| `CreateWidget`           | ~3     | **~2** | try/catch removed; gate+FireAndLog inlined |
| `DeleteWidget`           | ~3     | **~2** | same |
| `OnActionInvoked`        | ~3     | **~2** | same |
| `OnWidgetContextChanged` | ~3     | **~2** | same |
| `Activate`               | ~3     | **~2** | same |
| `HandleVerbAsync`        | ~7     | **~7** | arm count unchanged; `openRecent` body lifted out |
| `CreateNoteAsync`        | ~10    | **~10**| unchanged structurally; gained a `_gate.WithLockAsync` wrapper around the existing body |
| `RefreshAllActiveAsync`  | ~3     | **~3** | now delegates error handling to `FireAndLog` |
| `PushUpdate`             | ~4     | **~4** | unchanged |
| `FireAndLog` (new)       |  —     | **~4** | wraps `AsyncSafe.RunAsync` + `ContinueWith` |
| `SafePushUpdate` (new)   |  —     | **~2** | try/catch around `PushUpdate` |
| `HandleOpenRecentAsync` (new) | — | **~3** | lifted from `HandleVerbAsync` |
| **Sum (roughly)**        | **~39**| **~43**| **+4** |

### Verdict on "did the per-id gate + FireAndLog add or remove complexity?"

**Net: approximately neutral, with the complexity redistributed.** The five COM entrypoints each shed their inline try/catch (−5 branches), at the cost of three new helper methods (+9 branches collectively). The per-entrypoint code is now shorter and more uniform, but the class grew by 41 lines and three new methods, and every entrypoint now reads as `FireAndLog(() => _gate.WithLockAsync(id, async () => … ), id, "…", pushUpdateOnCompletion: true)` — one more level of nesting at the call site. The "God class" smell from v1 finding #1 **is unchanged**: same 8 responsibilities, now with two additional infrastructural ones (gate management, fire-and-log lifecycle) layered on top.

The sweep delivered safety (serialized state access, centralized error reporting) without delivering the structural simplification v1 called for.

---

## Verification of v1 action items

| v1 item                                                              | Applied? | Evidence |
|----------------------------------------------------------------------|:--------:|----------|
| v1 #1 — Extract Class ×5 on `ObsidianWidgetProvider`                 | ❌       | Still one type, still 8 responsibilities, now 436 lines. |
| v1 #2 — `CreateNoteAsync` Extract Method ×3 (inputs / paste / apply) | ❌       | Body is unchanged; only addition is the enclosing `_gate.WithLockAsync`. |
| v1 #3 — `PushUpdate` Extract Method / Strategy                       | ❌       | 3-arm `if/else if/else` on `(cli.IsAvailable, DefinitionId)` still inline. |
| v1 #4 — `HandleVerbAsync` Replace Conditional with Polymorphism      | ❌ (partial) | Still a switch, but `openRecent` was Extract-Method'd. Progress, not resolution. |
| v1 #5 — Fold `RefreshFolderCacheAsync` / `RefreshAllActiveAsync`     | ❌       | Both methods still duplicate the `ListFoldersAsync → state.CachedFolders/At → Save → PushUpdate` sequence. `RefreshAllActiveAsync` now also contains per-iteration `FireAndLog(…)` plumbing that `RefreshFolderCacheAsync` lacks — **duplication widened**. |
| v1 #6 — Move `RememberRecent` → `WidgetState`                        | ❌       | Still `private static void RememberRecent(List<string>, string, int)` on the provider (lines 423-428). |
| v1 #7 — Introduce Parameter Object `WidgetActionInputs`              | ❌       | `ParseInputs` still returns `Dictionary<string,string>` (lines 430-450); `CreateNoteAsync` still does 8 × `GetValueOrDefault("…")`. |
| v1 #8 — `ObsidianCli.RunAsync` Extract Method ×2                     | ❌       | File grew 172 → 263 lines (functional additions, not refactor). |
| v1 #9 — `IClipboardReader` + `WinRtClipboardReader`                  | ❌       | `TryReadClipboardText` is still a private static on the provider (lines 455-469). |
| v1 #10 — `CardDataBuilder.BuildFolderChoices` Substitute Algorithm   | ❌       | File shrank 105 → 92 lines; unrelated change, triple-loop shape retained (re-verify if revisiting). |
| v1 #11 — `QuickNoteForm` ctor Extract Method                         | ❌       | Unchanged (134 → 117 lines — unrelated). |
| v1 #12 — `NoteCreationService.CreateAsync` split daily/new paths     | ❌       | Unchanged (103 → 81 lines — unrelated shrinkage). |
| v1 #13 — Remove `CardStatus` / fix `IsComServerMode`                 | ❌       | Still present. |

As hypothesized in the brief: **this sweep added seams without re-organizing method bodies.**

---

## Updated ranked smells — what is now tractable

The gate + `AsyncSafe` change the risk calculus. Before, extracting a sub-service that touched `_store` risked racing against timer/action callbacks; now any sub-service that receives `_store`, `_gate`, and `_active` can safely perform `Get → mutate → Save` inside `_gate.WithLockAsync(id, …)`, and any fire-and-forget invocation is shielded by `AsyncSafe.RunAsync`. Several v1 items drop from "Low–Med" to "Low" risk, and two become nearly mechanical.

Ranking is **value × low-risk** given the new seams.

### 1. Extract Class `FolderCacheUpdater` — now the clearest win

- **Target:** `RefreshFolderCacheAsync` (lines 335-352) + `RefreshAllActiveAsync` (lines 359-380).
- **Why now tractable:** both methods already follow the exact contract the sub-service needs — "call CLI outside the gate, then for each widget id perform a `_gate.WithLockAsync(id, …)` store-update, then `SafePushUpdate(id)`". Injecting `(IObsidianCli, IStateStore, AsyncKeyedLock<string>, ConcurrentDictionary<string, WidgetSession>, Action<string> safePushUpdate)` into a new `FolderCacheUpdater` lets both public methods collapse to 3-line delegations.
- **Refactoring:** Extract Class + Extract Method (the duplicated `ListFolders → assign → save` body becomes the only method on the new class).
- **Effort:** S–M.
- **Risk:** Low. Behavior preserved; timer still ticks from the provider into the sub-service. No public COM surface changes.
- **Bonus:** the new class is unit-testable with fakes for all four dependencies — provider's only untested cluster today.

### 2. Move Method `RememberRecent` → `WidgetState.RememberRecentNote` / `RememberRecentFolder`

- **Target:** provider lines 288-289, 423-428.
- **Why now tractable:** unchanged from v1; flagged again because finding #3 (below) cannot cleanly extract `CreateNoteAsync`'s tail without this move happening first. Do this as the preparatory step.
- **Refactoring:** Move Method + Replace Magic Number with Symbolic Constant (the 16 and 8 become `WidgetState.MaxRecentNotes` / `MaxRecentFolders`).
- **Effort:** S.
- **Risk:** Very low. No test exists against `WidgetState` MRU semantics today; JSON shape (two `List<string>`) is unchanged.

### 3. Extract Class `NoteCreateCoordinator` (fka v1 #2 "Extract Method ×3")

- **Target:** `CreateNoteAsync` (lines 238-317).
- **Why now tractable:** the method has three clearly bounded phases separated by the gate boundary:
  1. **Build** (pre-gate): `ParseInputs` → `WidgetActionInputs` (v1 #7).
  2. **Apply** (inside `_gate.WithLockAsync`): map inputs over `state`, merge paste, build `NoteRequest`, call `_notes.CreateAsync`, reduce `result` into `state` (the 3-arm switch).
  3. **Post** (outside gate): conditional `FireAndLog(() => RefreshFolderCacheAsync, …)` for newly-created folders.
  
  With `_gate` now a field, a `NoteCreateCoordinator(_store, _gate, _notes, _active)` can own phase 2 end-to-end, and the provider's method becomes `await _createCoord.ExecuteAsync(session, actionData); _maybeRefresh(session.Id);` (~8 lines). The 3-arm `result.Status` switch moves into the coordinator as a private `ApplyResultToState`.
- **Refactoring:** Introduce Parameter Object (`WidgetActionInputs`) + Extract Method (`MergePendingPaste`, `BuildNoteRequest`, `ApplyResultToState`) + Extract Class (coordinator).
- **Effort:** M.
- **Risk:** Low. The `NoteCreationService`/`_notes.CreateAsync` boundary doesn't move — its tests stay green. Behavior around `_active.ContainsKey` / `_store.Get` / `_store.Save` is mechanically preserved by running under the same gate.
- **Prereq:** do #2 above first so `RememberRecent` is already on `WidgetState`.

### 4. Extract Class `WidgetCardRenderer` (fka v1 #3)

- **Target:** `PushUpdate` template/data selection (lines 389-406).
- **Why now tractable:** `SafePushUpdate` already wraps the call. Replacing `PushUpdate`'s body with `var (template, data) = _renderer.Render(session, state); _widgetManager.Update(widgetId, template, data);` is safe because `SafePushUpdate`'s try/catch still catches any renderer failure and the error flow via `FireAndLog.onError` still writes `LastError`.
- **Refactoring:** Extract Method `(template, data) = SelectPayload(session, state)` first (S, zero risk); later Extract Class `IWidgetCardRenderer` keyed on `DefinitionId` (M).
- **Effort:** S then M.
- **Risk:** Low. `CardDataBuilderTests` covers the data side.

### 5. Replace Conditional with Polymorphism — `HandleVerbAsync`

- **Target:** the 6-arm switch (lines 196-235).
- **Why now tractable:** `openRecent` was already lifted; each remaining arm is now small enough to become its own `IWidgetVerb.HandleAsync(session, data)` with the provider's `_cli`, `_store`, `_gate`, `_active` injected. `FireAndLog` at the entrypoint means verbs don't each need their own try/catch.
- **Refactoring:** `interface IWidgetVerb { string Name { get; } Task HandleAsync(WidgetSession, string?); }` + `Dictionary<string, IWidgetVerb>` registry. Unknown verbs fall through to a `UnknownVerb` that logs and no-ops.
- **Effort:** M (6 tiny classes).
- **Risk:** Low. Each verb becomes unit-testable in isolation — no `ObsidianWidgetProvider` instance needed.
- **Prereq:** do #3 first so `createNote`'s verb body is already a 1-line delegation to the coordinator.

### 6. Extract Interface `IClipboardReader` + `WinRtClipboardReader`

- **Target:** `TryReadClipboardText` (lines 455-469).
- **Why now tractable:** `pasteClipboard` verb body in `HandleVerbAsync` (lines 201-214) already runs inside `FireAndLog(() => _gate.WithLockAsync(…))` — swapping a `_clipboard.TryReadText()` call for the static is literally one line, and the existing `internal` ctor on `ObsidianWidgetProvider` already accepts injected dependencies (`log`, `store`, `cli`).
- **Refactoring:** add 4th optional ctor parameter `IClipboardReader? clipboard = null` with a `WinRtClipboardReader` default.
- **Effort:** S.
- **Risk:** Low. Opens the paste-merge path to unit testing for the first time.
- **Nice combo:** do this **with** #5 so the `pasteClipboard` verb class ships with the clipboard dependency already injected.

### 7. Substitute Algorithm — `CardDataBuilder.BuildFolderChoices`

- **Target:** `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs` (triple-filter pattern).
- **Why unchanged:** not provider-side; gate/AsyncSafe don't affect it. Still S, still Very Low risk, still covered by `CardDataBuilderTests`. Re-verify the exact line range before starting since the file changed 105 → 92 lines.
- **Effort:** S. **Risk:** Very low.

### 8. Extract Method ×2 — `ObsidianCli.RunAsync`

- **Target:** `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs` (172 → **263 lines**; grew since v1).
- **Why bumped down:** file grew by 91 lines — unclear whether growth added branches that a mechanical Extract Method can still cleanly carve up. Worth a re-read before scheduling; defer until #1–#3 ship.
- **Effort:** S (probably) or M (if the growth introduced new state). **Risk:** Low.

### 9. (Drop) Split `NoteCreationService.CreateAsync`

- **Target:** `src/ObsidianQuickNoteWidget.Core/Notes/NoteCreationService.cs` (103 → 81 lines).
- **Why dropped from top priorities:** file shrank 22 lines since v1. Re-read before acting; the pre-condition for v1's recommendation (two workflows bolted together) may have already been addressed by an unrelated change. Verify first.

### 10. Remove dead code — `CardStatus`, `Program.IsComServerMode`

- Unchanged from v1 #13. Still S, still Very low risk. Schedule after any of the above.

---

## Summary table of suggested refactorings (v2 ordering)

| # | Refactoring                                   | Target                                     | Effort | Risk      |
|---|-----------------------------------------------|--------------------------------------------|--------|-----------|
| 1 | Extract Class                                 | `FolderCacheUpdater` (folder-refresh duo)  | S–M    | Low       |
| 2 | Move Method + Replace Magic Number            | `RememberRecent` → `WidgetState`           | S      | Very low  |
| 3 | Introduce Parameter Object + Extract Class    | `NoteCreateCoordinator` / `WidgetActionInputs` | M   | Low       |
| 4 | Extract Method → later Extract Class          | `WidgetCardRenderer`                       | S / M  | Low       |
| 5 | Replace Conditional with Polymorphism         | `IWidgetVerb` registry                     | M      | Low       |
| 6 | Extract Interface + Move Method               | `IClipboardReader`                         | S      | Low       |
| 7 | Substitute Algorithm                          | `CardDataBuilder.BuildFolderChoices`       | S      | Very low  |
| 8 | Extract Method ×2                             | `ObsidianCli.RunAsync`                     | S–M    | Low       |
| 9 | Remove Dead Code                              | `CardStatus`, `IsComServerMode`            | S      | Very low  |

**Dropped from v1 top-3:** "Extract Class ×5 on provider" as a single item — replaced with the ordered sequence #1 → #2 → #3 → #4 → #5 → #6 above, which is the same destination approached as five independent small Extract Class / Extract Method steps, each shippable behind the unchanged COM contract.

**Dependency chain:** #2 precedes #3. #3 precedes #5 (so `createNote` is already a 1-line verb body). #6 composes cleanly with #5. #1, #4, #7–#9 are independent.

---

## Top 3 refactors to do next (best value × lowest risk, given the new seams)

1. **Extract Class `FolderCacheUpdater`** — the two folder-refresh methods are now nearly-identical and both already speak "outside-the-gate CLI call, then per-id gated write, then `SafePushUpdate`". This is the single extraction where the gate + AsyncSafe seams deliver the most leverage: the new class is a thin, fully-testable coordinator and the provider loses two of its eight responsibilities. Ship first.

2. **Move Method `RememberRecent` → `WidgetState` + Introduce Parameter Object `WidgetActionInputs`** — both are mechanical, neither depends on the gate, both are prerequisite for item 3, and together they eliminate the primitive-obsession and feature-envy called out in v1 #6 + #7. Small, isolated PRs.

3. **Extract Class `NoteCreateCoordinator`** — the longest behavioral method in the codebase (80 lines) and the one whose structure is now most obvious to carve, given the gate field makes the "inside-gate" phase a clean seam. After #2 lands, what remains in `CreateNoteAsync` is literally Build / Apply / Post-refresh — three phases that map 1:1 to three helper calls on the coordinator, with `ApplyResultToState` absorbing the 3-arm `result.Status` switch.

After those three, the file should drop below 300 lines and items #4–#6 become incremental rather than structural.

---

## Out of scope / handed off

- **Perf:** `JsonStateStore.Clone` via serialize/deserialize, `TryReadClipboardText` sync-over-async — still for `perf-profiler`.
- **Tests:** the new `AsyncKeyedLock` and `AsyncSafe` helpers have no direct coverage. After items #1 and #3 above land, the extracted classes are the first unit-testable seams provider-side — hand to `test-author`.
- **Docs:** the provider's XML doc still describes an 8-responsibility class; will need an update once #1–#3 ship → `doc-scribe`.
- **Bug-shaped:** `Program.IsComServerMode` remains dead and misleading; confirm packaging launch args → `bug-hunter` if behavior diverges from intent.

**Public API / contract diff if all v2 refactors applied:** none. (COM verbs, `IObsidianCli`, `IStateStore`, `ILog`, `WidgetState` JSON schema, CLI flags, embedded Adaptive Card resource names — all unchanged.)
