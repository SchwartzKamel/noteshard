# Refactorer Sweep — Obsidian Quick Note Widget

**Mode:** READ-ONLY. No source files were modified.
**Scope:** all `.cs` under `src/` (3 projects: `ObsidianQuickNoteWidget`, `ObsidianQuickNoteWidget.Core`, `ObsidianQuickNoteTray`).
**Behavior contract:** existing unit tests under `tests/ObsidianQuickNoteWidget.Core.Tests/` (9 test files covering Card, Cli parsers, NoteCreation, JsonStateStore, sanitizer/validator, frontmatter).
**Note on size:** the brief mentioned `ObsidianWidgetProvider.cs ~520 lines`; the file currently measures **395 lines** (still by far the largest source file). The smells called out below are still highly relevant.

---

## File size snapshot (excluding `obj/`)

| Lines | File |
| ----: | :--- |
| 395 | `ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs` |
| 172 | `ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs` |
| 134 | `ObsidianQuickNoteTray/QuickNoteForm.cs` |
| 113 | `ObsidianQuickNoteWidget/Program.cs` |
| 105 | `ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs` |
| 103 | `ObsidianQuickNoteWidget.Core/Notes/NoteCreationService.cs` |
|  93 | `ObsidianQuickNoteWidget.Core/State/JsonStateStore.cs` |
|  83 | `ObsidianQuickNoteTray/Program.cs` |

No file exceeds the 300-line "large class" threshold today, but `ObsidianWidgetProvider` is approaching it and has the worst responsibility spread.

---

## Findings (ranked by value × low-risk; high → low)

### 1. Large class / God class — `ObsidianWidgetProvider`
- **File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:19-395`
- **Smell:** Large Class with at least 8 distinct responsibilities mixed into one type:
  1. `IWidgetProvider` / `IWidgetProvider2` lifecycle (`CreateWidget`, `Activate`, …) — lines 51-121.
  2. Verb dispatch via switch — `HandleVerbAsync` lines 125-153.
  3. Note creation orchestration + state mapping — `CreateNoteAsync` lines 155-223.
  4. Folder cache refresh (per-widget + all-active timer) — lines 241-283.
  5. Card payload selection / `WidgetManager.UpdateWidget` call — `PushUpdate` lines 293-332.
  6. JSON `inputs` parsing — `ParseInputs` lines 341-361.
  7. Recent-list maintenance — `RememberRecent` lines 334-339.
  8. Clipboard access (WinRT) — `TryReadClipboardText` lines 366-380.
- **Refactoring:** **Extract Class** repeatedly. Suggested seams:
  - `WidgetActionRouter` (verb→handler map; addresses finding #4).
  - `NoteRequestMapper` (owns `ParseInputs` + `ParseBool` + state-fallback merge from `CreateNoteAsync` lines 157-185).
  - `WidgetStateMutator` (owns `RememberRecent` and post-creation state writes lines 189-213; addresses finding #6).
  - `FolderCacheRefresher` (owns `RefreshFolderCacheAsync` + `RefreshAllActiveAsync`; addresses finding #5).
  - `WidgetCardRenderer` (owns the template/data selection in `PushUpdate` lines 300-326).
  - `IClipboardReader` + `WinRtClipboardReader` (extracts `TryReadClipboardText`; addresses finding #9).
  After extraction the provider should be a thin coordinator (~120 lines) holding only COM-facing methods.
- **Effort:** **L** (multiple extractions; each individually small).
- **Risk:** **Low–Med.** Public COM contract unchanged; all extractions are internal. Tests live in `Core` and won't break since extractions move within the WinUI assembly. Recommended as a sequence of ≤80-line PRs, one Extract Class at a time, re-running `dotnet test` after each.

### 2. Long method + mixed concerns — `ObsidianWidgetProvider.CreateNoteAsync`
- **File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:155-223` (~70 lines, exceeds the 60-line guideline).
- **Smell:** Long Method: builds inputs, merges pending paste, builds `NoteRequest`, awaits `_notes.CreateAsync`, then performs a 25-line state-update with three branches based on `result.Status`.
- **Refactoring:**
  - **Extract Method** `BuildNoteRequest(WidgetSession, WidgetState, IDictionary<string,string>)` (lines 160-185).
  - **Extract Method** `MergePendingPaste(WidgetSession, ref body)` (lines 169-175).
  - **Replace Conditional with Polymorphism** is overkill for 3 branches; use **Extract Method** `ApplyResultToState(state, result, folder)` for lines 196-213, possibly a small `switch` expression keyed on `result.Status`.
- **Effort:** **S.**
- **Risk:** **Very low.** Pure intra-method extractions; behavior preserved by construction.

### 3. Long method — `ObsidianWidgetProvider.PushUpdate` + Card-template selection chain
- **File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:293-332` (40 lines, but the 14-line if/else if/else chain on lines 303-317 is the part that smells).
- **Smell:** Conditional dispatch on `(_cli.IsAvailable, session.DefinitionId)` to choose template + data builder. Today there are 3 cases; the next widget kind will turn this into a 4-way chain.
- **Refactoring:**
  - **Replace Conditional with Polymorphism** via an `IWidgetCardRenderer` strategy keyed on `DefinitionId` plus a special-case `CliMissingRenderer` selected when `!_cli.IsAvailable`. A simple `Dictionary<string, IWidgetCardRenderer>` is sufficient — full polymorphism is justified only once a third widget kind appears.
  - Until then, **Extract Method** `(template, data) = SelectCardPayload(session, state)` keeps `PushUpdate` focused on the COM call.
- **Effort:** **S** for Extract Method; **M** for full strategy.
- **Risk:** **Low.** Output bytes identical → covered indirectly by `CardDataBuilderTests`.

### 4. Switch chain that wants to be polymorphic — `HandleVerbAsync`
- **File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:125-153`.
- **Smell:** 6-arm `switch (verb)`; each verb is a self-contained unit of work with its own state mutation. New verbs (e.g. `togglePinned`, `clearRecents`) will pile on here.
- **Refactoring:** **Replace Conditional with Polymorphism**: `interface IWidgetVerb { string Name; Task HandleAsync(WidgetSession, string? data); }`, register in a `Dictionary<string, IWidgetVerb>`. Each verb becomes a tiny class — testable in isolation without spinning up the provider.
- **Effort:** **M** (6 classes + registration).
- **Risk:** **Low.** Public COM verbs unchanged. Ideal *after* finding #1's `WidgetActionRouter` extraction.

### 5. Duplication — folder-cache refresh paths
- **File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs`
  - `RefreshFolderCacheAsync(string)` lines 241-254.
  - `RefreshAllActiveAsync()` lines 261-283.
- **Smell:** Both fetch `ListFoldersAsync`, write `state.CachedFolders` + `state.CachedFoldersAt`, and call `PushUpdate`. The "all" variant is essentially the single variant in a loop with a shared CLI call.
- **Refactoring:** **Extract Method** `ApplyFoldersToState(string widgetId, IReadOnlyList<string> folders, DateTimeOffset at)` and have both callers funnel through it. The single-widget refresher then becomes a one-liner: `ApplyFoldersToState(id, await _cli.ListFoldersAsync(), DateTimeOffset.Now)`.
- **Effort:** **S.**
- **Risk:** **Very low.** No tests today against these methods (provider is COM-bound), but the move is mechanical.

### 6. Feature envy — recent-list maintenance lives outside `WidgetState`
- **File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:198-199, 334-339`.
- **Smell:** `RememberRecent(state.RecentNotes, …)` and `RememberRecent(state.RecentFolders, …)` are pure `WidgetState` mutations performed by the provider. Two collections + their MRU semantics + capacity belong with the data they touch.
- **Refactoring:** **Move Method** to `WidgetState` (or a small `MruList` value-helper):
  ```csharp
  state.RememberRecentNote(path);
  state.RememberRecentFolder(folder);
  ```
  with capacities encoded as constants on `WidgetState`. Eliminates the static helper from the provider.
- **Effort:** **S.**
- **Risk:** **Very low.** `WidgetState` has no behavior tests today, but JSON serialization shape is unchanged (still two `List<string>` properties).

### 7. Primitive obsession — `IDictionary<string,string>` for action inputs
- **Files:**
  - `ObsidianWidgetProvider.cs:341-361` (`ParseInputs`) and consumers on lines 160-167.
- **Smell:** `Dictionary<string,string>` is plumbed between `OnActionInvoked` and `CreateNoteAsync`. Type-unsafe lookups (`GetValueOrDefault("autoDatePrefix")` then re-parse), and the keys appear as magic strings in two places (here + the embedded card templates).
- **Refactoring:**
  - **Introduce Parameter Object** `WidgetActionInputs` (record) with typed fields and a single `WidgetActionInputs.FromJson(string?)` factory. Parsing + boolean coercion live there; `ParseBool` becomes private.
  - Keys remain centralized in one place (mitigates risk that a template rename silently breaks input mapping — that risk currently exists).
- **Effort:** **S.**
- **Risk:** **Low.** Public surface unchanged; the JSON wire format is identical (only the internal representation changes). Adds room for a unit test of input parsing — currently untested.

### 8. Long method — `ObsidianCli.RunAsync`
- **File:** `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs:34-94` (~60 lines).
- **Smell:** Process plumbing in one method: `ProcessStartInfo` build, start, async output capture, stdin write, timeout/cancellation, error mapping.
- **Refactoring:** **Extract Method**:
  - `BuildStartInfo(args, redirectStdin)` (lines 41-52).
  - `WaitWithTimeoutAsync(proc, timeout, ct)` (lines 80-91).
  Leaves `RunAsync` as ~25 lines of orchestration.
- **Effort:** **S.**
- **Risk:** **Low.** Method is exercised indirectly by `NoteCreationServiceTests` via the `IObsidianCli` fake; the real `ObsidianCli` has no direct tests, so refactor must be purely mechanical.

### 9. Tangled dependency / improper layering — WinRT clipboard inside the COM provider
- **File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:366-380` references `Windows.ApplicationModel.DataTransfer.Clipboard` directly and synchronously blocks an async API.
- **Smell:** UI-platform coupling buried in an otherwise platform-light coordinator; also untestable.
- **Refactoring:** **Extract Interface** `IClipboardReader` in `Core` (or a thin local interface) and **Move Method**: `WinRtClipboardReader` (lives in WinUI assembly), inject through the existing `internal` constructor on the provider. Mirrors the existing `IObsidianCli`/`IStateStore`/`ILog` pattern.
- **Effort:** **S–M.**
- **Risk:** **Low.** Behavior identical; gains the ability to unit-test paste flow.

### 10. Duplication in `BuildFolderChoices` — three nearly-identical filter loops
- **File:** `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs:56-83`.
- **Smell:** Three sequential `foreach` blocks each maintain "is this already added?" checks against the prior collections. As more folder sources are added (e.g. "frequently used"), each new loop will need to negative-check all earlier sources — quadratic shotgun surgery.
- **Refactoring:** **Substitute Algorithm** with a single `seen = HashSet<string>(OrdinalIgnoreCase)` and a small private `AppendIfNew(value, prefix)` helper. Three loops collapse to three `foreach` over `(source, prefix)` tuples.
- **Effort:** **S.**
- **Risk:** **Very low.** `CardDataBuilderTests` covers this output.

### 11. Long-ish constructor doing layout + wiring + I/O — `QuickNoteForm`
- **File:** `src/ObsidianQuickNoteTray/QuickNoteForm.cs:27-74`.
- **Smell:** Constructor mixes (a) form properties, (b) child layout, (c) event wiring, (d) state load, (e) async folder population. Standard WinForms smell.
- **Refactoring:** **Extract Method**: `BuildLayout()`, `WireEvents()`, `BeginPopulateFoldersAsync(IObsidianCli)`. `LoadState`/`SaveState` are already extracted — apply the same to construction.
- **Effort:** **S.**
- **Risk:** **Very low.** WinForms behavior preserved; no tests cover this path.

### 12. Mixed branches in `NoteCreationService.CreateAsync`
- **File:** `src/ObsidianQuickNoteWidget.Core/Notes/NoteCreationService.cs:24-89`.
- **Smell:** Two unrelated workflows share one method: "append to daily" (lines 32-45) and "create note" (lines 47-88). Reads as if the daily-append path is bolted onto the front.
- **Refactoring:** **Extract Method** `AppendDailyAsync(req, ct)` and `CreateNewNoteAsync(req, ct)`; `CreateAsync` becomes a 6-line dispatcher that returns the CLI-unavailable guard early then delegates. Optionally **Replace Conditional with Polymorphism** later if more "modes" appear.
- **Effort:** **S.**
- **Risk:** **Very low.** Well covered by `NoteCreationServiceTests`.

### 13. Anemic / dead-ish abstractions
- **`CardStatus` record** — `CardDataBuilder.cs:105`. Declared `public sealed record CardStatus(...)` and accepted as an optional parameter to `BuildQuickNoteData`, but no caller in `src/` ever passes a non-null `CardStatus` (all status flows through `WidgetState.LastStatus` / `LastError`). **Refactoring:** **Inline** the parameter (remove `status` param + record) **or** route the provider's `WriteStatus` through it. Today it's surface area without justification.
- **`IsComServerMode` always returns `true`** — `src/ObsidianQuickNoteWidget/Program.cs:102-112`. Dead branch (the `if` on line 35 can never go to the early-exit). **Refactoring:** **Inline Method** + delete the unreachable early return, or actually honor the flag. Either way, remove the lie.
- **`IStateStore`, `IObsidianCli`, `ILog`** — each has one production impl. They *do* earn their keep because they enable test doubles in `tests/`. Keep.
- **Effort:** **S** each.
- **Risk:** **Very low** (CardStatus inline) / **Low** (IsComServerMode — verify packaging launch args first; safest is to just delete the unreachable code, not change the default).

### 14. Minor — `JsonStateStore.Clone` via JSON round-trip
- **File:** `src/ObsidianQuickNoteWidget.Core/State/JsonStateStore.cs:91-92`.
- **Smell:** Clone-by-serialize is a structural code smell (copy semantics tied to JSON shape) but also a perf concern → **out of refactorer scope** (perf changes = behavior changes). **Hand off** to `perf-profiler` if hot. No structural action recommended now; flagging for awareness.

---

## Summary of suggested refactorings (Fowler vocabulary)

| # | Refactoring | Target | Effort | Risk |
|---|---|---|---|---|
| 1 | Extract Class ×5 | `ObsidianWidgetProvider` | L | Low–Med |
| 2 | Extract Method ×3 | `CreateNoteAsync` | S | Very low |
| 3 | Extract Method (later: Strategy) | `PushUpdate` | S/M | Low |
| 4 | Replace Conditional with Polymorphism | `HandleVerbAsync` | M | Low |
| 5 | Extract Method | folder-refresh duo | S | Very low |
| 6 | Move Method | `RememberRecent` → `WidgetState` | S | Very low |
| 7 | Introduce Parameter Object | `WidgetActionInputs` | S | Low |
| 8 | Extract Method ×2 | `ObsidianCli.RunAsync` | S | Low |
| 9 | Extract Interface + Move Method | `TryReadClipboardText` | S–M | Low |
| 10 | Substitute Algorithm | `BuildFolderChoices` | S | Very low |
| 11 | Extract Method ×3 | `QuickNoteForm` ctor | S | Very low |
| 12 | Extract Method ×2 | `NoteCreationService.CreateAsync` | S | Very low |
| 13 | Inline Method / Remove Dead Code | `CardStatus`, `IsComServerMode` | S | Very low |

---

## Top 3 refactor opportunities (best value × lowest risk)

1. **Extract Class on `ObsidianWidgetProvider`** (finding #1). Highest value: every other provider-side smell (#2, #3, #4, #5, #6, #7, #9) becomes easier or trivial once the god class is split. Recommended sequence: `FolderCacheRefresher` → `WidgetCardRenderer` → `WidgetActionRouter` → `NoteRequestMapper` → `IClipboardReader`. Each step ships independently behind unchanged COM contracts; `dotnet test` is the safety net.

2. **Move Method `RememberRecent` to `WidgetState` + Introduce Parameter Object `WidgetActionInputs`** (findings #6, #7). Two tiny, mechanical refactors that together eliminate the most obvious feature-envy and primitive-obsession in the codebase, give `WidgetState` actual behavior to test, and don't touch any public contract or wire format.

3. **Extract Method on `CreateNoteAsync` and `NoteCreationService.CreateAsync`** (findings #2, #12). These are the two longest *behavioral* methods in the codebase. Pure intra-method extractions, fully covered (12) or trivially covered (2) by existing tests, and they make the surrounding code amenable to the larger Extract Class work in #1.

---

## Out-of-scope / handed off

- **Perf**: `JsonStateStore.Clone` via serialize/deserialize, `TryReadClipboardText` `.GetAwaiter().GetResult()` block — hand to `perf-profiler`.
- **Bug-shaped**: `Program.IsComServerMode` is dead-and-misleading — confirm packaging behavior before deleting; consider `bug-hunter` if behavior diverges from intent.
- **Docs**: `ObsidianWidgetProvider` XML doc is accurate today but will need an update once Extract Class lands → `doc-scribe`.
- **Tests**: provider, `ObsidianCli` (real), and `WidgetState` MRU semantics have no direct coverage. After #1/#6 extraction, hand to `test-author` for unit tests on the new types.

**Public API / contract diff if all suggested refactors applied:** none. (COM verbs, `IObsidianCli`, `IStateStore`, `ILog`, `WidgetState` JSON schema, CLI flags, embedded Adaptive Card resource names — all unchanged.)
