# perf-profiler — v3 Static Performance Review

> **Hypothesis-only report.** No benchmarks were run for this pass. Per the
> perf-profiler archetype's "no measurement, no change" rule, nothing below
> justifies a code change on its own. The BenchmarkDotNet harness recommended
> in v1 and v2 still does not exist in this repo; that remains the top
> prerequisite before any optimization lands.

**Target:** `C:\Users\lafia\csharp\obsidian_widget` (post-v2 refactor, plus
the `folderNew` precedence logic and the `style: "compact"` ChoiceSet tweaks
in the Adaptive Card templates).
**Scope:** Re-confirm whether the v3 changes introduced any new perf concern,
and restate the top-3 hotspot hypotheses.

---

## 1. Perf assessment of the v3 deltas

### 1a. `folderNew` precedence in `HandleCreateNoteAsync`

File: `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:250-254`

```csharp
var folderNew = inputs.GetValueOrDefault("folderNew")?.Trim();
var folder = !string.IsNullOrEmpty(folderNew)
    ? folderNew
    : inputs.GetValueOrDefault("folder") ?? state.LastFolder;
```

- **Classification:** None — not on any measurable hot path.
- **Cost analysis:** One extra `Dictionary<string,string>.TryGetValue`, one
  `string.Trim()` (allocates only when trimming is needed — zero-alloc when
  the input has no leading/trailing whitespace on modern .NET), one
  `string.IsNullOrEmpty` check. This runs **once per user "Create" click**,
  which is by far the coldest path in the provider (user-driven, already
  dominated by three `Process.Start` spawns — see H4). The added cost is
  on the order of tens of nanoseconds per invocation, vs. the ~100+ ms of
  process-spawn cost that follows.
- **Verdict:** **No new perf concern.** Does not change any v1/v2 hotspot
  ranking. Does not affect the per-tick `RefreshAllActiveAsync` loop or
  `PushUpdate` cadence.

### 1b. `style: "compact"` ChoiceSet in `QuickNote.large.json` / `QuickNote.medium.json`

Files: `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/QuickNote.{large,medium}.json`
(folder + template pickers now render as compact dropdowns instead of expanded
radio lists.)

- **Classification:** None on the perf-profiler axis. This is a **rendering
  hint for the host's Adaptive Card renderer**, not code the C# side runs.
- **Cost analysis — server side:** The JSON template is still the same shape
  and size (the `style` field adds ~20 bytes per ChoiceSet). `CardTemplates.Load`
  still reads the same embedded resource on every `PushUpdate` (H2) — the
  extra bytes are negligible. `CardDataBuilder.BuildQuickNoteData` is
  **unchanged**: it still materializes the full `folderChoices` array for
  *every* `PushUpdate` regardless of whether the host will render them as
  compact or expanded. Switching to `compact` does **not** let us lazy-build
  the choice list, because the host still needs the full array up front to
  populate the dropdown.
- **Cost analysis — host/rendering side:** Out of scope for perf-profiler
  (that's inside WindowsAppSDK's Adaptive Cards renderer). If anything, a
  compact dropdown is cheaper to render than an expanded radio list for
  long folder lists — but that's a client-side win we can't measure from
  this repo.
- **Verdict:** **No new perf concern introduced.** Worth noting: the change
  does **not** invalidate H3 (quadratic `Contains` + `JsonSerializerOptions`
  churn in `BuildFolderChoices`) — that hotspot is triggered by the *data*
  build, which runs on every `PushUpdate` irrespective of the ChoiceSet
  rendering style.

### 1c. Secondary observations

- No new `_store.Save` or `_store.Get` sites introduced by the v3 deltas.
  H1 write-amplification surface is unchanged vs v2.
- No change to `PushUpdate` call frequency. H2 cadence is unchanged vs v2.
- No change to `Process.Start` call sites. H4 user-path triple-spawn is
  intact; timer-path single spawn is intact.
- No change to `AsyncKeyedLock` usage patterns. v2 NH1/NH2/NH3 assessments
  carry over unmodified.

---

## 2. Reconfirmation of H1–H4

| Hypothesis | v1 | v2 | v3 | Notes |
|------------|----|----|----|-------|
| **H1** — `JsonStateStore` write-amplification (indented JSON, clone via serialize+deserialize, full-file rewrite per `Save`, N writes per `RefreshAllActiveAsync` tick) | top | top | **top (unchanged)** | Still the largest single hot path by expected CPU + IO + allocs. v3 added zero new `Save` sites. |
| **H2** — `CardTemplates.Load` re-reads embedded-resource stream on every `PushUpdate` | #2 | #2 (slightly worse post-v2 `pushUpdateOnCompletion:true`) | **#2 (unchanged)** | v3 did not touch `CardTemplates` or `PushUpdate` call count. Cheapest fix in the repo: static `ConcurrentDictionary<string,string>` cache primed in ctor. |
| **H3** — `CardDataBuilder.BuildFolderChoices` quadratic `Contains` + per-call `JsonSerializerOptions` allocation | #3–#4 | #3–#4 | **demoted slightly** | Same code, same call frequency. Neither `folderNew` precedence nor `style: compact` alters the data-build path — but see v3 note below. |
| **H4** — `Process.Start` churn (timer fan-out + user triple-spawn) | top-tier (user path + timer) | timer path fixed; user path still 3× spawns | **unchanged from v2** | `folderNew` flows into the 2nd spawn (`CreateNoteAsync`) identically to `folder`. Still dominant for create-note p99. |

**All four hypotheses still apply.** v3 did not fix, worsen, or shift any
of them in any measurable way.

### Minor note on H3 post-v3

Now that `folderNew` can inject an **arbitrary** folder path that was not in
`PinnedFolders`, `RecentFolders`, or `CachedFolders`, a subsequent `PushUpdate`
will see that path enter `RecentFolders` via the normal `RememberRecent`
flow (if wired — check `CreateNoteAsync` → `HandleCreateSuccessAsync`). This
does **not** change H3's asymptotic cost; it merely means `RecentFolders`
can grow slightly faster in practice. Still bounded (typical cap = 8).

---

## 3. Top 3 (re-ranked post-v3 — identical to v2)

1. **H1 — `JsonStateStore` write amplification.** Compact JSON on disk,
   record-based `with`-clone (eliminate `Serialize→Deserialize` round-trip),
   debounced `Persist`, single `Persist` at end of `RefreshAllActiveAsync`.
   Expected the largest single win once a bench harness exists.
2. **H2 — `CardTemplates.Load` per `PushUpdate`.** One-shot static cache
   keyed by resource name, primed in a static ctor or on first access.
   Trivial change, mild real win, hits the interactive hot path directly.
3. **H4 (user path) — triple `Process.Start` inside `CreateNoteAsync`.**
   Cache `vault info=path` for the process lifetime (reduces 3 → 2 spawns
   on the user-visible path). Long-lived CLI session remains a
   measurement-gated option only if (a) does not hit the latency target.

(H3 remains in the backlog as #4, unchanged from v2.)

---

## 4. Classification

- **Primary class (unchanged):** IO-bound (state persistence + CLI process
  spawns) with secondary allocation pressure (Clone round-trip, template
  re-reads, DOM JSON builds, fresh `JsonSerializerOptions` per call).
- **No new contention, GC, or startup regressions introduced by v3.**
- **No new hotspot hypotheses promoted by v3.** The `folderNew` logic and
  the ChoiceSet rendering-hint change are both sub-threshold on any
  plausible measurement.

## 5. Recommended next action

Unchanged from v1 and v2: **commit a BenchmarkDotNet harness** covering
`StateStoreBench` (H1), `CardTemplatesBench` (H2), `CliSpawnBench` /
`NoteCreationBench` (H4), and a macro `CreateNoteBench` wrapping
`ObsidianWidgetProvider` with an in-memory `IStateStore`. Capture baseline
numbers. Only then attack H1 → H2 → H4 in order, re-measuring after each
change and rejecting any that misses the 5 % floor on the target metric.

**No code changes are recommended by this report.**
