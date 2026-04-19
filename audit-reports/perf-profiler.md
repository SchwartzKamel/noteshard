# perf-profiler — Static Performance Review

**Target:** `C:\Users\lafia\csharp\obsidian_widget` (ObsidianQuickNoteWidget COM widget provider + Core)
**Scope:** Static code review only. **No measurements were taken.** No benchmark harness exists in this repository.
**Status disclosure:** _No measurements; all findings are hypotheses pending benchmark._ Per the perf-profiler archetype's "no measurement, no change" rule, **none of the proposed fixes should be applied until a reproducible benchmark is committed and baseline + after numbers are captured.** This report is a prioritized list of candidate hotspots to instrument first.

## Workload shape (inferred)

- COM server process hosted by the Windows Widget Host. Long-lived while any widget is pinned.
- Interactive widget updates on user input (`OnActionInvoked`) — p99 latency matters (user-facing).
- Background timer every 2 min (`RefreshAllActiveAsync`) refreshing folder cache.
- Persisted state at `%LocalAppData%\ObsidianQuickNoteWidget\state.json`, log at `log.txt`.
- External dependency: `obsidian` CLI process launched per call.

## Suggested benchmark harness (prerequisite)

Before touching any of the below, add a `bench/ObsidianQuickNoteWidget.Benchmarks/` BenchmarkDotNet project that covers:

- `JsonStateStore.Get` / `Save` round-trip with a realistic `WidgetState` (≈50 cached folders, 16 recent notes, 8 pinned).
- `CardDataBuilder.BuildQuickNoteData` with the same state shape, varying `showAdvanced`.
- `CardTemplates.Load` cold vs. warm (to confirm embedded-resource read cost).
- `ObsidianCliParsers.ParseFolders` with 100/1000-line inputs.
- An end-to-end `PushUpdate`-style micro-bench that stitches Load + Build (wrapping an in-memory `IStateStore`).
- Process-spawn overhead probe: `Process.Start` with `obsidian --version` ×N, measuring wall-clock p50/p99.

Also add an allocation dimension (`[MemoryDiagnoser]`) — several of the hypotheses below are allocation-pressure, not CPU.

---

## Hotspot hypotheses (ranked by expected impact)

### H1 — `JsonStateStore`: full-file rewrite + double-serialize on every `Save`  ⭐ top

- **Files:**
  - `src/ObsidianQuickNoteWidget.Core/State/JsonStateStore.cs:42-51` (`Save` → `Persist`)
  - `src/ObsidianQuickNoteWidget.Core/State/JsonStateStore.cs:76-89` (`Persist`)
  - `src/ObsidianQuickNoteWidget.Core/State/JsonStateStore.cs:91-92` (`Clone` = serialize-then-deserialize)
  - `src/ObsidianQuickNoteWidget.Core/State/JsonStateStore.cs:32-40` (`Get` also clones)
- **Classification:** IO-bound + allocation pressure.
- **Mechanism:** Every `Save` (called from `CreateWidget`, `OnWidgetContextChanged`, `WriteStatus`, `CreateNoteAsync`, `RefreshFolderCacheAsync`, and once per widget inside `RefreshAllActiveAsync`) re-serializes the **entire** widget dictionary with `WriteIndented = true` and writes it via `File.WriteAllText` + atomic rename. `Get` additionally clones by `Serialize → Deserialize`. The `RefreshAllActiveAsync` path does this in a per-widget loop on a timer → with N widgets pinned, one tick does N full-file rewrites and 2N JSON round-trips for clones.
- **Expected impact:**
  - CPU: O(total widgets × state size) per tick; `WriteIndented` ≈ 2× the bytes and CPU vs. compact.
  - Allocations: `Clone` via serialize+deserialize produces a large transient allocation per `Get` (there are 6+ `Get` calls per user action in `CreateNoteAsync`).
  - IO: disk sync + atomic rename on every status-bar change.
  - On a cold/slow disk or AV-scanned `LocalAppData`, tail latency can dominate interactive updates.
- **Suggested benchmark:**
  - BDN `StateStoreBench.Save_20Widgets`, `Get_Hot`, `RefreshAllActive_Simulation` with `[MemoryDiagnoser]`.
  - Macro: time a full `OnActionInvoked("createNote")` end-to-end including `PushUpdate`.
- **Proposed fix (pending numbers):**
  - Drop `WriteIndented = true` for the on-disk form (keep a debug toggle).
  - Replace `Clone` with a hand-written copy constructor or `with`-expression on a record type — eliminates the serialize-deserialize round-trip entirely.
  - Debounce `Persist()`: coalesce writes on a short timer (e.g., 250 ms) so a burst of `Save` calls yields one disk write. Flush synchronously on `DeleteWidget` and process exit.
  - In `RefreshAllActiveAsync`, mutate all widgets under one lock and `Persist` **once** at the end instead of per-widget.

### H2 — `CardTemplates.Load`: disk/manifest-resource read + LINQ scan on every `PushUpdate`  ⭐

- **Files:**
  - `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardTemplates.cs:14-25` (`Load`)
  - `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardTemplates.cs:27-32` (`LoadForSize`)
  - Caller: `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:303-316` (every `PushUpdate`)
- **Classification:** IO-bound (embedded-resource stream) + allocation.
- **Mechanism:** `PushUpdate` is invoked on every user action, every folder-cache refresh, and every timer tick × widget. Each call re-executes `GetManifestResourceNames()` (returns a fresh `string[]`), LINQ `.FirstOrDefault`, opens a `Stream`, and `ReadToEnd`s the full template JSON. The template is immutable for the process lifetime.
- **Expected impact:** Small per call (tens to low hundreds of µs) but multiplicative with H1/H4 and on the user-visible hot path. Also produces steady allocation pressure (string arrays, StreamReader buffers, final template string).
- **Suggested benchmark:** BDN `CardTemplatesBench.Load_Medium` cold vs. warm; trace allocations.
- **Proposed fix:** Replace `Load` with a static `ConcurrentDictionary<string,string>` cache (or `Lazy<string>` per template). Eagerly prime on construction.

### H3 — `CardDataBuilder.BuildQuickNoteData`: quadratic `Contains` + N `JsonSerializerOptions` allocations

- **Files:**
  - `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs:13-44` (`BuildQuickNoteData`, `ToJsonString(new JsonSerializerOptions…)`)
  - `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs:56-83` (`BuildFolderChoices`: O(P·R·F) `Contains`)
- **Classification:** CPU + allocation.
- **Mechanism:**
  - `BuildFolderChoices` iterates `PinnedFolders`, then `RecentFolders` with a linear `PinnedFolders.Contains(r, OIC)`, then `CachedFolders` with two linear `Contains` calls. With a large vault (hundreds of folders) this is O(pinned × cached + recent × cached), and each `Contains` uses an ordinal-insensitive comparer that can't hit a hashed path.
  - `new JsonSerializerOptions { WriteIndented = false }` is allocated on every call (line 43) — `JsonSerializerOptions` is expensive to construct because it triggers converter-cache setup; the idiomatic pattern is one static cached instance per shape.
  - Uses `JsonObject`/`JsonArray` DOM (slower and allocation-heavier than `Utf8JsonWriter` or serialized records).
- **Expected impact:** With 5 pinned + 8 recent + 200 cached folders, the `Contains` cost is small but the entire function runs inside every `PushUpdate`. Allocation pressure from DOM + fresh `JsonSerializerOptions` likely dominates.
- **Suggested benchmark:** BDN `CardDataBench.BuildQuickNoteData_Realistic` with `[MemoryDiagnoser]`; vary folder counts (10/100/500).
- **Proposed fix:**
  - Static `WriteIndentedFalseOptions`.
  - Build `HashSet<string>(pinned, OrdinalIgnoreCase)` and `HashSet<string>(recent, OrdinalIgnoreCase)` once, use O(1) lookups.
  - Consider writing straight to a `Utf8JsonWriter`/`MemoryStream` and returning the UTF-8 string once — avoids the intermediate `JsonObject` tree entirely.

### H4 — `ObsidianCli.RunAsync`: `Process.Start` per call; no long-lived pipe

- **Files:**
  - `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs:34-94` (`RunAsync`)
  - `src/ObsidianQuickNoteWidget.Core/Notes/NoteCreationService.cs:67,77,84` (three process spawns per note creation: `GetVaultRootAsync`, `CreateNoteAsync`, `OpenNoteAsync`)
  - `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:261-283` (`RefreshAllActiveAsync` on 2-min timer)
- **Classification:** IO-bound (process creation) + syscall overhead.
- **Mechanism:** Every CLI call spawns a fresh `obsidian` child. On Windows, `CreateProcess` + CLR initialization for a child interpreter is typically **50–150 ms** cold. The "Create note" user flow issues **three** sequential spawns (vault root → create → open), so user-perceived latency is 3× the per-spawn cost. A folder-refresh on timer adds one spawn every 2 min. There is no process reuse / no long-lived stdin-pipe protocol.
- **Expected impact:** Dominant contributor to end-to-end "create note" p99 latency. Order hundreds of ms.
- **Suggested benchmark:** Micro: `CliBench.SpawnEmpty` — time `Process.Start("obsidian", "--version")`. Macro: `NoteCreationBench.CreateAsync_FullPath` against a real vault.
- **Proposed fix (multi-step, each measured):**
  1. **Cache `vault info=path`.** The vault root is effectively static for the process lifetime; call once on startup and invalidate on failure. Eliminates 1 of 3 spawns per note.
  2. **Fire-and-forget `open`.** Already partially done (`_ = _cli.OpenNoteAsync(...)`) but still spawns; acceptable.
  3. **Investigate long-lived CLI:** if Obsidian CLI supports a REPL/pipe mode (doesn't currently, based on code), wrap in a `CliSession` that keeps one child alive with redirected stdio. Significant work — only justified if (1) doesn't hit the latency target.
  4. Remove `RefreshAllActiveAsync`'s extra spawn overlap risk (see H5).

### H5 — `RefreshAllActiveAsync`: re-entrancy / overlap on slow CLI

- **File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:30-47, 261-283`
- **Classification:** Concurrency / latency hazard (not a raw perf bug until it overlaps).
- **Mechanism:** The `Timer` fires on a background thread every 2 min regardless of whether the previous tick finished. If `obsidian folders` takes >2 min on a huge vault or a stalled CLI, ticks overlap: two concurrent `ListFoldersAsync` processes, each writing the same state file, contend on `JsonStateStore._gate` and disk. Additionally, `_ = RefreshAllActiveAsync()` is unawaited — exceptions disappear into the void beyond the inner try/catch.
- **Expected impact:** Under normal operation: negligible. Under a slow/unresponsive vault: pathological (unbounded process fan-out).
- **Suggested benchmark:** Not a bench target; add a stress test that simulates a 3-min-sleeping CLI stub and asserts only one in-flight call.
- **Proposed fix:** Gate with `Interlocked.CompareExchange(ref _refreshInFlight, 1, 0)` or use `PeriodicTimer` with manual re-arm after completion. Either way, read `_active.Keys` under a snapshot and write **one** persist call at the end (see H1).

### H6 — `FileLog.Write`: synchronous IO + `DateTimeOffset.Now` formatting + `FileInfo` stat on every line

- **Files:** `src/ObsidianQuickNoteWidget.Core/Logging/FileLog.cs:28-56`
- **Classification:** IO-bound + allocation on a hot path.
- **Mechanism:**
  - `Info` is called inside `PushUpdate` (line 319: logs `templateLen`/`dataLen`) and on every COM callback. Each call:
    - Formats `DateTimeOffset.Now` with `:O` (round-trip format, allocates ≥30 chars).
    - Interpolates a line (allocation).
    - Takes the `_gate` lock.
    - `Roll()` always runs `File.Exists` + `new FileInfo(_path).Length` — two filesystem stats per write.
    - `File.AppendAllText` opens/writes/closes a file handle per call (no buffered writer kept open).
  - Worst case: disk syscalls serialize the entire provider on the log lock.
- **Expected impact:** Small per line, but this is on the user-visible action path. Measurable in allocs/op. On AV-scanned `LocalAppData`, the `File.AppendAllText` open/close can spike.
- **Suggested benchmark:** `FileLogBench.Info_SteadyState` with `[MemoryDiagnoser]`, varying parallelism.
- **Proposed fix:**
  - Keep a `StreamWriter` open with `AutoFlush = false`, flush on a timer or every N lines / on shutdown.
  - Check size every N writes or only when `AppendAllText` returns (avoid stat per line).
  - Consider `Channel<string>` + a single consumer task (async logger) so the hot path just enqueues.

### H7 — `ObsidianCliParsers.ParseFolders`: LINQ chain allocates on every refresh

- **File:** `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCliParsers.cs:34-45`
- **Classification:** Allocation pressure; CPU minor.
- **Mechanism:** `Split → Select → Where → Distinct → OrderBy → ToArray` allocates: the intermediate string array from `Split`, a `Select` iterator, a `Where` iterator, a `Distinct` set, an `OrderBy` buffer, and the final array. For a 500-folder vault, this is ≈6 allocations × N lines worth of work, once every 2 min per refresh.
- **Expected impact:** Low frequency (every 2 min) → low aggregate impact. Likely _not_ worth changing unless BDN says otherwise. Listed for completeness.
- **Suggested benchmark:** `ParsersBench.ParseFolders_500` with `[MemoryDiagnoser]`.
- **Proposed fix (only if hot):** Single-pass: walk the string with `MemoryExtensions.Split`/`IndexOf` over a `ReadOnlySpan<char>`, dedup via `HashSet<string>(OrdinalIgnoreCase)`, then `list.Sort(...)`.

### H8 — `EscapeContent`: five sequential `string.Replace` passes on note body

- **File:** `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCliParsers.cs:54-63`
- **Classification:** Allocation; CPU minor.
- **Mechanism:** Allocates a new string on each `Replace`. For a 5 KB body this is 5 full copies. Called once per create → very cold path.
- **Expected impact:** Negligible. Cold path (user-driven, not timer-driven). Listed for completeness; likely below the 5 % floor.
- **Suggested benchmark:** `CliBench.Escape_5KBBody`.
- **Proposed fix:** Single-pass `StringBuilder` (or `vectorized` scan). **Do not implement without bench justification.**

### H9 — Startup: `ObsidianCli` constructor walks `%PATH%` synchronously

- **Files:** `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs:26-30, 157-171` (`ResolveExecutable`); called from `ObsidianWidgetProvider` ctor → COM activation hot path.
- **Classification:** Startup latency.
- **Mechanism:** On COM cold-start, the provider constructor calls `ResolveExecutable` which iterates every `PATH` directory × 4 extensions doing `File.Exists`. On a machine with 30 PATH entries, that's 120 `File.Exists` calls on a cold filesystem — measurable startup hit on first widget render.
- **Expected impact:** One-time per process-start; Widget Host restarts the COM server when all widgets unpin and then repin, so user-visible. Likely 10–50 ms cold, under 1 ms warm.
- **Suggested benchmark:** `StartupBench.ResolveExecutable` on a cold cache (hard to simulate deterministically — measure mean ± stddev with `IterationSetup` clearing the Windows file cache is out of scope; settle for warm numbers + a note).
- **Proposed fix (if hot):** Lazy — only resolve on first `IsAvailable`/`RunAsync` call. Or probe `Environment.SpecialFolder.LocalApplicationData`/`ProgramFiles\Obsidian` first since that's the most likely location.

### H10 — Startup / Program.cs: `File.AppendAllText` to proof-of-life log before logger init

- **File:** `src/ObsidianQuickNoteWidget/Program.cs:19-28`
- **Classification:** Startup latency; negligible.
- **Mechanism:** One synchronous file open/append on every COM-server launch.
- **Expected impact:** Trivial. Keep for diagnostic value; not a hotspot.

### H11 — `RememberRecent`: `RemoveAll` + `Insert(0, …)` + `RemoveRange`

- **File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:334-339`
- **Classification:** CPU / allocation.
- **Mechanism:** `List<string>.Insert(0, ...)` shifts all elements; `RemoveAll` with a predicate boxes nothing but iterates fully. List caps at 16 (notes) / 8 (folders) — tiny.
- **Expected impact:** Negligible. Cold path. Not worth optimizing.

### H12 — Logging interpolation in `PushUpdate`: always runs even when not needed

- **File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:319`
- **Classification:** Allocation.
- **Mechanism:** `$"PushUpdate id={widgetId} def={session.DefinitionId} size={session.Size} templateLen={template?.Length ?? 0} dataLen={data?.Length ?? 0}"` is always materialized (`ILog` has no level-check API). Happens on every update.
- **Expected impact:** Small; contributes to steady allocation.
- **Suggested fix:** Add `bool IsInfoEnabled` or `LoggerMessage`-style source-generated logging if ever we adopt MEL. Likely below the 5 % floor.

---

## Overall classification

- **Primary bottleneck class:** IO-bound (state persistence + CLI process spawns + log appends) with secondary **allocation pressure** (DOM JSON, LINQ chains, `JsonSerializerOptions` churn).
- **Not seen:** Lock contention (single-writer state store, COM is STA-pumped), GC gen-2 pressure (no evidence of large long-lived allocations — but measure), binary size concerns.

## Recommended sequence

1. **Build the benchmark harness first** (see "Suggested benchmark harness" above). Commit it. Capture baselines.
2. Instrument an end-to-end macro trace of `OnActionInvoked("createNote")` → `PushUpdate`. Record p50/p99 wall-clock.
3. Only then attack hotspots **one at a time**, in H1 → H2 → H4 → H3 → H6 order, re-measuring after each, rejecting any change below the 5 % floor per the archetype's guardrails.

## Remaining caveats

- No workload evidence exists yet that any of this is actually user-visibly slow. The widget likely feels instant in normal use. These are _potential_ hotspots, selected by static reasoning about call frequency × per-call cost.
- **Do not merge optimizations based on this document alone.** Per the archetype: "No measurement, no change."
