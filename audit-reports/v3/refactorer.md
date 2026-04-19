# Refactorer Sweep v3 — Obsidian Quick Note Widget

**Mode:** READ-ONLY. No source files were modified.
**Baseline priors:** `audit-reports/refactorer.md` (v1, file was 395 lines) and `audit-reports/v2/refactorer.md` (v2, file was 436 lines).
**Scope:** unchanged — all `.cs` under `src/`; behavior contract remains the `tests/ObsidianQuickNoteWidget.Core.Tests/` suite.

---

## What changed since v2

One targeted feature landed in `ObsidianWidgetProvider.CreateNoteAsync`: a `folderNew` input gate. Two new lines (plus whitespace) now front the existing `folder` fallback chain at lines 251–254:

```csharp
var folderNew = inputs.GetValueOrDefault("folderNew")?.Trim();
var folder = !string.IsNullOrEmpty(folderNew)
    ? folderNew
    : inputs.GetValueOrDefault("folder") ?? state.LastFolder;
```

Everything else flagged in v2 (the 8-responsibility provider, the `RememberRecent` static, `ParseInputs` → `Dictionary<string,string>`, the 3-arm `result.Status` switch, the folder-refresh duplication, the `HandleVerbAsync` switch, the inline `TryReadClipboardText`) is **unchanged**.

---

## Re-measurement of `ObsidianWidgetProvider.cs`

| Metric                                         | v1   | v2   | **v3**   | Δ v2→v3 |
|------------------------------------------------|-----:|-----:|---------:|--------:|
| Raw lines                                      | 395  | 436  | **487**  | **+51** |
| `CreateNoteAsync` body length                  | ~70  | ~80  | **~82**  | **+2**  |
| Methods on the type                            | 13   | 16   | 16       | 0       |
| Distinct responsibilities                      | 8    | 8    | **8**    | 0       |
| Longest method in file (still `CreateNoteAsync`) | yes | yes | **yes**  | —       |

### Breakdown of the v2→v3 file-level delta

The provider grew 51 lines since v2; only **2** of those landed inside `CreateNoteAsync` (the `folderNew` gate). The remaining ~49 lines are elsewhere in the provider (additional ceremony around `FireAndLog` / post-create folder-cache refresh — see lines 311–319 — and minor reshuffles). That growth is **not** a smell introduced by this sweep; it's additional wiring on the existing coordinator, which reinforces v2's #1 finding rather than changing the ranking.

### Is `CreateNoteAsync` still tractable?

**Yes — arguably more tractable than in v2.** The method is now 82 lines, with an even crisper three-phase shape:

1. **Parse** (lines 240, outside gate): `ParseInputs(actionData)`.
2. **Apply** (lines 245–309, inside `_gate.WithLockAsync`): map inputs → build `NoteRequest` → `_notes.CreateAsync` → 3-arm reduce into `state` → `_store.Save`.
3. **Post** (lines 311–319, outside gate): read-back `_store.Get` + conditional `FireAndLog(() => RefreshFolderCacheAsync(...))`.

The `folderNew` addition lives entirely in phase 2's "map inputs" block (lines 250–260) and is a pure local-variable assignment — it does not cross phase boundaries, does not introduce new branching outside the existing `!string.IsNullOrEmpty` test, and does not couple to any new field on the provider. Mechanically, the Extract Method / Extract Class plan from v2 #3 absorbs it with zero adjustment: the same `WidgetActionInputs` parameter object gains one more property (`FolderNew`), the "build folder" logic becomes a 2-line expression inside the coordinator, and nothing else in the plan shifts.

**Tractability verdict per v2 criteria:** S (Extract Method ×3) still holds; M (full Extract Class `NoteCreateCoordinator`) still holds. The 2-line growth does not push either past its effort bucket.

### Why this bumps `CreateNoteAsync` up the priority ladder

Two reinforcing signals now point to starting with `CreateNoteAsync` rather than the folder-refresh pair:

1. **It is the single longest method in the largest file in the repo, and it just got longer.** Every subsequent feature touching note creation (templates, tag pickers, more input fields) will land here first. Each addition will repeat the `inputs.GetValueOrDefault("…") ?? fallback` pattern (now up to 9 instances across the method) and further stretch the same monolith.
2. **The gate seam is now load-bearing for the method.** The whole body is inside `_gate.WithLockAsync(session.Id, async () => { … })`. Extracting the inside-gate phase into a `NoteCreateCoordinator.ApplyAsync(session, inputs)` is a *pure* Extract Method — the gate stays on the provider, and the coordinator doesn't need to know it exists. The refactor therefore commits to no new coupling; it only subtracts.

So v2 #3 ("Extract Class `NoteCreateCoordinator`") should promote above v2 #1 ("Extract Class `FolderCacheUpdater`") in v3's ordering — see below.

---

## Updated ranked smells (v3)

Ranking criterion unchanged: **value × low-risk**, given the gate + `AsyncSafe` seams.

### 1. Extract Class `NoteCreateCoordinator` (was v2 #3) — **now top**

- **Target:** `CreateNoteAsync` lines 238–320.
- **Why promoted:** it is the longest method in the file, it grew in v3, and its three-phase shape (Parse / inside-gate Apply / outside-gate Post-refresh) is now the clearest in the codebase. The `folderNew` addition further motivated this: the "map inputs" block at lines 250–260 is now the densest concentration of primitive-obsession in the provider (9 `GetValueOrDefault` calls, 3 `ParseBool` coercions, 1 folder-fallback chain).
- **Refactoring steps (unchanged from v2):**
  1. Introduce Parameter Object `WidgetActionInputs` — absorbs `ParseInputs` + `ParseBool` and adds a typed `FolderNew` property.
  2. Extract Method `BuildNoteRequest(session, state, inputs)` — lines 250–278.
  3. Extract Method `MergePendingPaste(session, ref body)` — lines 262–268.
  4. Extract Method `ApplyResultToState(state, result, folder)` — lines 282–306 (absorbs the 3-arm `result.Status` switch).
  5. Extract Class `NoteCreateCoordinator(_store, _gate, _notes, _active)` — owns phase 2 end-to-end; provider's `CreateNoteAsync` collapses to ~8 lines (Parse, delegate, optional FireAndLog).
- **Effort:** M. **Risk:** Low. The `NoteCreationService`/`_notes.CreateAsync` boundary does not move; its tests stay green. Each sub-step is independently shippable and independently green.
- **Prereq:** do #2 (Move Method `RememberRecent`) first so the 3-arm switch doesn't need to reach into two provider-static helpers from inside the new coordinator.

### 2. Move Method `RememberRecent` → `WidgetState` + Replace Magic Number

- **Target:** provider lines 291–292 (call sites) and 426–431 (definition).
- **Why here:** unchanged from v2. Still the cheapest refactor in the codebase, still the gating prerequisite for #1's final step, still gives `WidgetState` actual behavior to unit-test for the first time.
- **Effort:** S. **Risk:** Very low.

### 3. Extract Class `FolderCacheUpdater` (was v2 #1)

- **Target:** `RefreshFolderCacheAsync` (lines 338–…) + `RefreshAllActiveAsync` (lines 362–…).
- **Why demoted:** still a clean extraction, still Low risk, still valuable — but `CreateNoteAsync` now eclipses it in both absolute size and per-feature growth pressure. The folder-refresh duo also didn't grow in v3, while `CreateNoteAsync` did. Ship after #1 + #2.
- **Effort:** S–M. **Risk:** Low.

### 4. Extract Class `WidgetCardRenderer`

- **Target:** `PushUpdate` lines 385–… (template/data selection chain).
- Unchanged from v2 #4.
- **Effort:** S then M. **Risk:** Low.

### 5. Replace Conditional with Polymorphism — `HandleVerbAsync`

- **Target:** lines 194–235 (still a 6-arm switch, with `openRecent` already lifted in v2).
- Unchanged from v2 #5. After #1 lands, `createNote`'s arm is already a 1-line delegation, which makes the strategy conversion that much cleaner.
- **Effort:** M. **Risk:** Low.

### 6. Extract Interface `IClipboardReader`

- Unchanged from v2 #6. `TryReadClipboardText` lives at line 458.
- **Effort:** S. **Risk:** Low.

### 7. Substitute Algorithm — `CardDataBuilder.BuildFolderChoices`

- **File:** `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs` (105 → 92 → **107** lines). File moved in v3 by unrelated additions; re-verify the triple-filter block before acting. Still S / Very low risk.

### 8. Extract Method ×2 — `ObsidianCli.RunAsync`

- **File:** `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs` (172 → 263 → **297** lines; also note a newly-split `ObsidianCliParsers.cs` at 142 lines — **parser extraction already partially done**, and is a direct materialization of the v1 #8 suggestion via Extract Class rather than Extract Method). Re-read before scheduling; whatever remains in `RunAsync` itself is a smaller target now.

### 9. Remove dead code — `CardStatus`, `Program.IsComServerMode`

- Unchanged. S / Very low risk.

---

## Summary table (v3 ordering)

| # | Refactoring                                   | Target                                     | Effort | Risk      | Δ vs v2 |
|---|-----------------------------------------------|--------------------------------------------|--------|-----------|---------|
| 1 | Introduce Parameter Object + Extract Method ×3 + Extract Class | `NoteCreateCoordinator` / `WidgetActionInputs` | M | Low | **promoted** (was #3) |
| 2 | Move Method + Replace Magic Number            | `RememberRecent` → `WidgetState`           | S      | Very low  | unchanged (#2) |
| 3 | Extract Class                                 | `FolderCacheUpdater`                       | S–M    | Low       | **demoted** (was #1) |
| 4 | Extract Method → later Extract Class          | `WidgetCardRenderer`                       | S / M  | Low       | unchanged (#4) |
| 5 | Replace Conditional with Polymorphism         | `IWidgetVerb` registry                     | M      | Low       | unchanged (#5) |
| 6 | Extract Interface + Move Method               | `IClipboardReader`                         | S      | Low       | unchanged (#6) |
| 7 | Substitute Algorithm                          | `CardDataBuilder.BuildFolderChoices`       | S      | Very low  | unchanged (#7) |
| 8 | Extract Method ×2 (re-scope)                  | `ObsidianCli.RunAsync` (remainder)         | S      | Low       | partial credit — `ObsidianCliParsers.cs` already split |
| 9 | Remove Dead Code                              | `CardStatus`, `IsComServerMode`            | S      | Very low  | unchanged (#9) |

**Dependency chain:** #2 precedes #1. #1 precedes #5 (so `createNote`'s verb body is a 1-line delegation). #6 composes cleanly with #5. #3, #4, #7–#9 are independent.

---

## Top 3 refactors to do next

1. **Move Method `RememberRecent` → `WidgetState` + Replace Magic Number (`MaxRecentNotes = 16`, `MaxRecentFolders = 8`).** Smallest possible PR in the codebase, zero public-surface movement, gives `WidgetState` its first unit-testable behavior, and unblocks #2 below without touching any method bodies in `CreateNoteAsync` yet.

2. **Extract Class `NoteCreateCoordinator` + Introduce Parameter Object `WidgetActionInputs`.** Now the highest-leverage provider-side refactor: `CreateNoteAsync` is the single longest method in the largest file, it grew again in v3 (folderNew gate: +2 lines, +1 primitive-obsession site), and its three-phase shape maps 1:1 onto Parse / coordinator.ApplyAsync / optional post-refresh. Ship as the sub-step sequence (Parameter Object → 3× Extract Method → Extract Class) so each step independently re-runs `dotnet test` green.

3. **Extract Class `FolderCacheUpdater`.** Still the cleanest sub-service extraction — the two refresh methods remain near-duplicates and already speak the gate + `SafePushUpdate` idiom perfectly. Demoted in v3 only because #2 now outweighs it; it is still the correct third step and the point at which the provider drops below 300 lines.

After those three, the provider file should land near 250–280 lines with 5 of the 8 responsibilities extracted, and items #4–#6 become incremental polish rather than structural surgery.

---

## Out of scope / handed off

- **Perf:** `JsonStateStore.Clone` via serialize/deserialize (now 107 lines), `TryReadClipboardText` sync-over-async — still `perf-profiler`.
- **Tests:** `AsyncKeyedLock` (124 lines, still no direct tests), the forthcoming coordinator, and `WidgetState` MRU semantics — hand to `test-author` once #1/#2 ship.
- **Docs:** provider XML doc still implies an 8-responsibility coordinator; update once #1–#3 ship → `doc-scribe`.
- **Bug-shaped:** `Program.IsComServerMode` still dead/misleading → `bug-hunter` to confirm packaging args, then delete.

**Public API / contract diff if all v3 refactors applied:** none. COM verbs, `IObsidianCli`, `IStateStore`, `ILog`, `WidgetState` JSON schema, CLI flags, embedded Adaptive Card resource names — all unchanged.
