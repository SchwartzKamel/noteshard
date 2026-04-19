# perf-profiler — v2 Static Performance Review

> **Hypothesis-only report.** No benchmarks were run for this pass. Per the
> perf-profiler archetype's "no measurement, no change" rule, none of the
> findings below justify a code change on their own — they identify *where to
> instrument next* after the v1 sweep's AsyncKeyedLock / widgetId-threading
> refactor. A BenchmarkDotNet harness still does not exist in this repo; that
> remains the top prerequisite from v1.

**Target:** `C:\Users\lafia\csharp\obsidian_widget` (post-refactor)
**Scope:** Static re-review of v1 findings H1 / H2 / H4 under the new
`AsyncKeyedLock<string>` gate + widgetId-threading, plus three new hotspot
hypotheses introduced by the refactor itself.

---

## 1. Verification vs v1 report

### H1 — `JsonStateStore` IO / Clone: **neutral** (shape unchanged; one new read, same write count)

Re-read `src/ObsidianQuickNoteWidget.Core/State/JsonStateStore.cs` — `Get`
still clones via `Serialize→Deserialize`, `Save` still does the full-file
`WriteIndented = true` + atomic-rename on every call, `Persist` is still
O(entire dictionary). **Nothing in the refactor addressed H1.**

Call-count delta introduced by the refactor (searched all `_store.Save` /
`_store.Get` sites in `ObsidianWidgetProvider.cs`):

| Path                              | v1 Save calls | v2 Save calls | Notes |
|-----------------------------------|--------------:|--------------:|-------|
| `CreateWidget`                    | 1             | 1             | now inside `_gate.WithLockAsync` — same count |
| `OnWidgetContextChanged`          | 1             | 1             | same |
| `DeleteWidget` (Delete, not Save) | 0             | 0             | same |
| `CreateNoteAsync`                 | 1             | 1 + 1 extra `Get` at L312 outside gate | net +1 Clone on happy path |
| `RefreshFolderCacheAsync`         | 1             | 1             | same |
| `RefreshAllActiveAsync` tick      | N             | N             | still 1 per widget per tick |
| `FireAndLog.onError` (new)        | 0             | 1 per failure | new gated Get+Save path when any op throws |
| `HandleVerbAsync "pasteClipboard"`| 0 (v1 set on session only) | 1 | new gated Save for status text |

Net assessment: **H1 is not better**. Happy path has one extra `Clone` (the
post-`CreateNoteAsync` `_store.Get(session.Id)` at line 312 outside the gate,
used only to decide whether to fire-and-forget a folder refresh — a cheap
bool-on-empty check that does not need a fresh state copy). Error path and
paste path added new Save sites. Write volume per timer tick is unchanged
because the per-widget loop survived the refactor.

**Still the top candidate.** Fixes proposed in v1 (compact JSON on disk,
record-based `with`-clone, debounced `Persist`, single `Persist` at end of
`RefreshAllActiveAsync`) all still apply.

### H2 — `CardTemplates.Load` on every `PushUpdate`: **slightly worse**

`CardTemplates.Load` is unchanged — still does
`GetManifestResourceNames() → LINQ → Stream.ReadToEnd` per call.

What changed: `FireAndLog(..., pushUpdateOnCompletion: true)` is now wired into
`CreateWidget`, `OnActionInvoked`, `OnWidgetContextChanged`, and `Activate`,
via a `ContinueWith` on the completion task. `RefreshAllActiveAsync` also
passes `pushUpdateOnCompletion: true` per widget. That means **every single
gated operation now ends with a PushUpdate**, where v1 only pushed after
specific verbs. The template read frequency is therefore a small multiple
higher than before — same per-call cost, more calls. H2 moves up the priority
list relative to v1.

Fix (still): one-shot cache (`ConcurrentDictionary<string,string>` or
`Lazy<string>` per template name); prime in ctor. Trivial change, but still
gated on a bench confirming the per-call microsecond cost.

### H4 — `CreateProcess` churn: **mixed (timer path improved; user path unchanged)**

Timer path (`RefreshAllActiveAsync`, `ObsidianWidgetProvider.cs:359-380`) was
refactored: the CLI call is now hoisted **out** of the per-widget loop —
`var folders = (await _cli.ListFoldersAsync()...).ToList()` runs once per
tick, then the loop only persists to each widget's state. In v1 that spawn
would have fanned out once per widget. This is a real improvement: process
spawns per tick dropped from **O(N widgets)** to **1**.

User path (`CreateNoteAsync`) is **unchanged** — still 3 sequential spawns
(`GetVaultRootAsync` → `CreateNoteAsync` → `OpenNoteAsync`), which remains
the dominant contributor to create-note p99.

Net: timer CPU/IO pressure better; user-perceived latency unchanged. V1's
proposed fixes for the user path (cache `vault info=path`, keep existing
fire-and-forget `open`, long-lived CLI session if Obsidian ever supports it)
all still apply.

---

## 2. New hotspot hypotheses (introduced by the v2 refactor)

### NH1 — `AsyncKeyedLock<string>` per-key Entry allocation / disposal churn

- **File:** `src/ObsidianQuickNoteWidget.Core/Concurrency/AsyncKeyedLock.cs:73-115`
- **Classification:** Allocation pressure (micro); contention (low).
- **Mechanism:** Every `WithLockAsync` call goes through `Acquire`, which either
  `TryGetValue`s (takes a CLR `lock` on `Entry.SyncRoot`, bumps `RefCount`) or
  constructs a new `Entry { SemaphoreSlim(1,1), SyncRoot, … }`. When
  `RefCount` drops to 0 in `Release`, the entry is removed from the dictionary
  and the `SemaphoreSlim` is disposed. Because COM callbacks arrive one at a
  time per widget **and** each completes before the next user interaction, the
  common pattern is: acquire → release (RefCount → 0) → **dispose** → next
  call re-allocates a fresh `Entry` + `SemaphoreSlim`. Effectively one
  `SemaphoreSlim` allocation per gated operation in steady state.
- **Scale:** For N widgets with the typical low-frequency COM callback rate,
  this is trivial — sub-microsecond allocation cost. The refcounted disposal
  is correct and keeps `_entries` bounded, so memory is fine. **Not a hotspot
  at realistic N.**
- **Bench question:** `AsyncKeyedLockBench.AcquireRelease_HotKey` with
  `[MemoryDiagnoser]` — measure bytes/op vs a fixed-size `SemaphoreSlim[]`
  pool keyed by `widgetId.GetHashCode() % capacity`.
- **Fix (only if hot in bench):** Keep `Entry` alive for a grace period after
  `RefCount` hits 0 (lazy disposal on next add, or time-based), or precreate a
  bounded semaphore pool and live with occasional unrelated-widget
  serialization. Do not change without numbers.

### NH2 — `RefreshAllActiveAsync` serial per-widget gate awaits

- **File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:365-379`
- **Classification:** Latency (background).
- **Mechanism:** After hoisting the CLI spawn, the per-widget loop now does
  `await FireAndLog(() => _gate.WithLockAsync(id, … Get+Save …), …)`
  sequentially. Each iteration waits for the previous widget's `Save` (full
  file rewrite — see H1) to finish before acquiring the next id's gate. With
  N widgets and a ~5–15 ms state write each, the tick takes N × write-time
  to drain. `pushUpdateOnCompletion: true` also fires N sequential
  `PushUpdate`s on the same thread.
- **Scale:** For N ≤ 5 (realistic), sub-100 ms per tick at 2-min cadence —
  **not user-visible, not material**. For N ≥ 20 on a slow/AV-scanned disk it
  could approach a second, still non-interactive. Classify as "not a hotspot
  worth fixing yet" but flag for re-examination if H1 is ever fixed (reducing
  per-Save cost would make per-id gate acquisition dominate instead, at which
  point parallelism becomes cheap).
- **Bench question:** `ProviderBench.RefreshAllActive_N=1,5,20` end-to-end
  timer tick latency with an in-memory `IStateStore` to isolate gate + loop
  cost from H1.
- **Fix (only if hot):** Replace the serial `await` loop with
  `Task.WhenAll(_active.Keys.Select(id => _gate.WithLockAsync(id, …)))` — the
  keyed lock already supports different keys running concurrently, so this is
  safe. Or, pair with the v1 H1 fix ("one `Persist` at the end") by mutating
  `_cache` under the gate for each id but deferring disk write to a single
  `Persist()` at loop end — kills both bottlenecks in one change.

### NH3 — `ObsidianCli.ResolveExecutable` now touches registry + multiple FS probes on ctor

- **File:** `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs:213-258`
  (+ `IObsidianCliEnvironment.GetObsidianProtocolOpenCommand` opening
  `HKCU\Software\Classes\obsidian\shell\open\command` via `Registry.CurrentUser.OpenSubKey`)
- **Classification:** Startup latency.
- **Mechanism:** v1's `ObsidianCli` ctor did a PATH scan only (v1 H9). v2 now
  probes, in order: env var `OBSIDIAN_CLI` → `%ProgramFiles%\Obsidian\Obsidian.(com|exe)`
  → `%LOCALAPPDATA%\Programs\Obsidian\Obsidian.(com|exe)` → `HKCU` registry
  open → PATH scan. In the **common case** (Obsidian installed to the
  per-machine or per-user default), this short-circuits after 1–2 `File.Exists`
  calls — **faster than v1's full PATH walk**. In the **uninstalled-or-custom
  case**, it now additionally opens the registry before falling back to PATH
  — marginally slower worst case. Result is cached in `_resolvedExe` for the
  process lifetime, so the cost is paid **once** per COM-server start.
- **Bounded by cache?** Yes. `_resolvedExe` is assigned once in the ctor, read
  only on `IsAvailable` and `RunAsync`. There is no re-resolution path.
- **Scale:** Cold case: registry open is typically <1 ms (HKCU is in-memory
  for the current user). Known-path `File.Exists` is ~100 µs cold, nanoseconds
  warm. Total expected ctor cost: well under 10 ms in the common case — an
  **improvement** over v1's H9 PATH walk in the typical install layout.
- **Fix:** None needed. Optionally: lazy-initialize `_resolvedExe` behind
  `Lazy<string?>` so the registry/fs probes don't run if no `IsAvailable`
  check ever happens (e.g. if the widget never actually uses the CLI on first
  tick). Cost-benefit is unclear without bench.

---

## 3. Top 3 (re-ranked post-refactor; hypotheses only)

1. **H1 — `JsonStateStore` write amplification** (carried from v1; unchanged by
   refactor; still the largest single hot path by expected CPU + IO + allocs).
2. **H2 — `CardTemplates.Load` per `PushUpdate`** (slightly worse post-refactor
   because `pushUpdateOnCompletion: true` is now threaded through most
   operations). Cheapest fix in the repo — trivial cache, mild real win.
3. **H4 (user path) — triple `Process.Start` inside `CreateNoteAsync`**
   (refactor improved the timer path but left the user-visible create-note
   path intact; still dominates p99 for "click Create → note appears").

**Demoted vs v1:** H4-timer (now only 1 spawn/tick instead of N).
**Promoted from nowhere:** nothing — NH1/NH2/NH3 are all classified
sub-threshold absent bench data.

---

## 4. Classification

- Primary class (unchanged): IO-bound (state persistence + CLI spawns) with
  secondary allocation pressure (Clone round-trip, template reads, DOM JSON).
- No new contention or GC concerns introduced by `AsyncKeyedLock` at realistic
  N. The refactor cleaned up a real fan-out problem in the timer path (H4)
  without making any other v1 hotspot meaningfully worse.

## 5. Recommended next action

Still the same as v1: **commit a BenchmarkDotNet harness** covering
`StateStoreBench`, `CardTemplatesBench`, `CardDataBench`, `CliSpawnBench`,
and a macro `CreateNoteBench` wrapping `ObsidianWidgetProvider` with an
in-memory store. Without numbers, none of the above should drive a change.
