# Test-Runner Suite-Health Report — v2

**Target:** `C:\Users\lafia\csharp\obsidian_widget`
**Archetype:** test-runner (read-only sweep — no code edits)
**Spec:** `C:\Users\lafia\.copilot\agents\test-runner.md`
**Prior report:** `audit-reports/test-runner.md` (132/132, deterministic)

---

## TL;DR

- **199 / 199 passing**, **0 failed**, **0 skipped** — Release, `--no-build`.
- **Zero flakes** across 3 back-to-back runs. Identical pass/fail/skip counts each run.
- Test count grew **132 → 199** (+67) since prior sweep — discovery healthy, no silent under-discovery.
- Suite duration: ~**400–411 ms** reported per run (≈2 s wall clock incl. host startup).
- **One test crosses no hard 1 s threshold**, but `PerWidgetGateTests.SameKey_Serializes` at **367 ms** is the new single-test outlier (3.7× the next slowest). Concurrency-gate test, expected to do real waits — flag, don't fail.
- Build: **0 warnings, 0 errors** on Release. `dotnet format --verify-no-changes` exits **0** (clean).
- Coverage: `coverlet.collector` 6.0.4 still wired in tests csproj. `dotnet-coverage` global tool still **not installed** (unchanged from prior sweep).

---

## Determinism verdict

**DETERMINISTIC — zero flake.**

| Run | Result  | Passed | Failed | Skipped | Total | Reported duration |
| --- | ------- | ------ | ------ | ------- | ----- | ----------------- |
| 1   | Passed! | 199    | 0      | 0       | 199   | 411 ms            |
| 2   | Passed! | 199    | 0      | 0       | 199   | 407 ms            |
| 3   | Passed! | 199    | 0      | 0       | 199   | 402 ms            |

- Identical counts across all 3 runs. Reported duration variance ≤ 9 ms (~2%).
- No ordering, timing, or state-leak instability observed.
- No skipped, ignored, or quarantined tests.

---

## Build & format

- `dotnet build -c Release --nologo` → **0 Warning(s), 0 Error(s)**, ~2.85 s elapsed. All 4 projects: `ObsidianQuickNoteWidget.Core`, `ObsidianQuickNoteWidget.Core.Tests`, `ObsidianQuickNoteTray`, `ObsidianQuickNoteWidget`.
- `dotnet format --verify-no-changes` → **exit 0** (only the standard "Warnings were encountered while loading the workspace" notice, which is informational and does not indicate formatting drift).

---

## Slow-test audit (> 1 s threshold)

Captured per-test durations from a single `--logger:"console;verbosity=detailed"` run.

- **Tests above 1000 ms: 0.** No hard slow-test flags.
- One test is a noticeable outlier — `PerWidgetGateTests.SameKey_Serializes` at 367 ms — but is by design exercising serialized concurrent access and well below the 1 s flag.

### Slowest 10 tests

| #  | Duration | Test                                                                                                  |
| -- | -------- | ----------------------------------------------------------------------------------------------------- |
| 1  | 367 ms   | `PerWidgetGateTests.SameKey_Serializes`                                                               |
| 2  | 101 ms   | `JsonStateStoreTests.Delete_RemovesWidget`                                                            |
| 3  |  61 ms   | `NoteCreationServiceTests.Create_OpenAfterCreate_OpensNote`                                           |
| 4  |  34 ms   | `FileLogTests.Write_CrlfInMessage_CollapsesToEscapedLiteral_SingleLine`                               |
| 5  |  29 ms   | `FrontmatterBuilderTests.ParseTagsCsv_Normalizes(csv: "#tag1, #tag2", expected: ["tag1","tag2"])`     |
| 6  |  27 ms   | `CardDataBuilderTests.QuickNoteData_RendersError`                                                     |
| 7  |  26 ms   | `CardTemplatesTests.LoadForSize_UnknownSize_FallsBackToMedium(size: "xl")`                            |
| 8  |  26 ms   | `NoteCreationServiceTests.Create_HappyPath`                                                           |
| 9  |  23 ms   | `FileLogTests.Write_TabPreserved`                                                                     |
| 10 |  22 ms   | `PerWidgetGateTests.EntryIsRemoved_WhenLastHolderReleases`                                            |

Notes:
- Top contributors are concurrency primitives (`PerWidgetGate*`), filesystem I/O (`JsonStateStore`, `NoteCreationService`, `FileLog`), and the first-touched theory in each fixture (warm-up cost). All within reason.
- No handoff to **perf-profiler** warranted.

---

## Coverage tooling status

- ✅ `coverlet.collector` **6.0.4** referenced in `tests/ObsidianQuickNoteWidget.Core.Tests/ObsidianQuickNoteWidget.Core.Tests.csproj`.
  Run: `dotnet test -c Release --collect:"XPlat Code Coverage"`.
- ⚠️ `dotnet-coverage` global tool **not installed** (`dotnet tool list -g` returns no rows; unchanged from prior sweep).
- No `coverlet.msbuild` reference (collector path only — fine for VSTest-driven runs).
- No `.runsettings` / `coverlet.runsettings` at repo root — no exclusion patterns or thresholds defined. Non-blocking.

---

## Diff vs. prior sweep

| Metric                  | Prior (132)     | This sweep (199)              | Delta                                |
| ----------------------- | --------------- | ----------------------------- | ------------------------------------ |
| Total tests             | 132             | 199                           | **+67**                              |
| Passed / Failed / Skip  | 132 / 0 / 0     | 199 / 0 / 0                   | clean                                |
| Reported duration / run | 102–117 ms      | 402–411 ms                    | +~290 ms (more tests + concurrency)  |
| Flakes                  | 0               | 0                             | unchanged                            |
| Slowest test            | 65 ms           | 367 ms (`SameKey_Serializes`) | new concurrency test outlier         |
| Build warnings/errors   | 0 / 0           | 0 / 0                         | unchanged                            |
| `dotnet format` clean   | (n/a in prior)  | yes                           | confirmed                            |
| `coverlet.collector`    | 6.0.4 present   | 6.0.4 present                 | unchanged                            |
| `dotnet-coverage` (g)   | not installed   | not installed                 | unchanged                            |

New test files exercised since prior report include `PerWidgetGateTests`, `FileLogTests` (visible via the slow-list), accounting for most of the +67.

---

## Guardrail compliance

- Read-only sweep. **No source files modified.** No tests skipped, weakened, or deleted.
- Only artifacts written: this report and a scratch file `audit-reports/v2/_test-detailed.txt` (verbose run log, safe to delete).

---

## Recommendations (non-blocking)

1. **No action needed on suite health.** Green, fast, deterministic.
2. `PerWidgetGateTests.SameKey_Serializes` (367 ms) is by far the heaviest test; if it ever drifts past ~1 s, hand to **perf-profiler** to confirm the gate's wait isn't ballooning.
3. Optional: install `dotnet-coverage` global tool and/or add a `coverlet.runsettings` if CI wants merged coverage reports with exclusions/thresholds.
