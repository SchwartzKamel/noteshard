# Test-Runner Suite-Health Report — v3

**Target:** `C:\Users\lafia\csharp\obsidian_widget`
**Archetype:** test-runner (read-only sweep — no code edits)
**Spec:** `C:\Users\lafia\.copilot\agents\test-runner.md`
**Prior reports:** v1 `audit-reports/test-runner.md` (132/132), v2 `audit-reports/v2/test-runner.md` (199/199)

---

## TL;DR

- **199 / 199 passing**, **0 failed**, **0 skipped** — Release, `--no-build`. Matches v2 baseline exactly.
- **Zero flakes** across 3 back-to-back runs. Identical counts, <6% duration variance.
- Build: **0 warnings, 0 errors** (Release). `dotnet format --verify-no-changes` → **exit 0** (clean).
- No test crosses the 1 s slow-test threshold. Slowest is still `PerWidgetGateTests.SameKey_Serializes` at **372 ms** (≈ v2's 367 ms — stable).
- No source files modified; only artifacts written are under `audit-reports/v3/`.

---

## Determinism verdict

**DETERMINISTIC — zero flake.**

Command per run: `dotnet test -c Release --nologo --no-build`

| Run | Result  | Passed | Failed | Skipped | Total | Reported duration |
| --- | ------- | ------ | ------ | ------- | ----- | ----------------- |
| 1   | Passed! | 199    | 0      | 0       | 199   | 419 ms            |
| 2   | Passed! | 199    | 0      | 0       | 199   | 416 ms            |
| 3   | Passed! | 199    | 0      | 0       | 199   | 397 ms            |

- Identical pass/fail/skip counts across all 3 runs.
- Reported duration variance ≤ 22 ms (~5%).
- No ordering, timing, or state-leak instability observed.
- No `[Fact(Skip=…)]` / `[Theory(Skip=…)]` / quarantined tests.

---

## Build & format

- `dotnet build -c Release --nologo` → **0 Warning(s), 0 Error(s)**, ~1.63 s elapsed. All 4 projects build: `ObsidianQuickNoteWidget.Core`, `ObsidianQuickNoteWidget.Core.Tests`, `ObsidianQuickNoteTray`, `ObsidianQuickNoteWidget`.
- `dotnet format --verify-no-changes` → **exit 0**. Only the standard informational "Warnings were encountered while loading the workspace" notice appears; no formatting drift.

---

## Slow-test audit (> 1 s threshold)

Per-test durations captured from a single `--logger:"console;verbosity=detailed"` run.

- **Tests above 1000 ms: 0.** No hard slow-test flags.
- One test remains a modest outlier — `PerWidgetGateTests.SameKey_Serializes` at 372 ms — by design exercising serialized concurrent access. Stable vs. v2 (367 ms).

### Slowest 10 tests (v3 detailed run)

| #  | Duration | Test                                                                                             |
| -- | -------- | ------------------------------------------------------------------------------------------------ |
| 1  | 372 ms   | `PerWidgetGateTests.SameKey_Serializes`                                                          |
| 2  |  87 ms   | `JsonStateStoreTests.Delete_RemovesWidget`                                                       |
| 3  |  62 ms   | `NoteCreationServiceTests.Create_OpenAfterCreate_OpensNote`                                      |
| 4  |  28 ms   | `FrontmatterBuilderTests.ParseTagsCsv_Normalizes(csv: "#tag1, #tag2", …)`                        |
| 5  |  28 ms   | `FileLogTests.Write_NonAsciiPreserved_Utf8Roundtrip`                                             |
| 6  |  26 ms   | `NoteCreationServiceTests.Create_HappyPath`                                                      |
| 7  |  25 ms   | `FileLogTests.Write_CrlfInMessage_CollapsesToEscapedLiteral_SingleLine`                          |
| 8  |  23 ms   | `PerWidgetGateTests.DifferentKeys_RunConcurrently`                                               |
| 9  |  23 ms   | `CardDataBuilderTests.QuickNoteData_RendersError`                                                |
| 10 |  22 ms   | `FileLogTests.Write_TabPreserved`                                                                |

Notes:
- Top contributors are concurrency primitives (`PerWidgetGate*`), filesystem I/O (`JsonStateStore`, `NoteCreationService`, `FileLog`), and first-hit theory warm-ups. All within reason.
- No handoff to **perf-profiler** warranted.

---

## Diff vs. prior sweeps

| Metric                  | v1 (132)     | v2 (199)    | v3 (this)   | Delta v2→v3            |
| ----------------------- | ------------ | ----------- | ----------- | ---------------------- |
| Total tests             | 132          | 199         | 199         | **0** (stable)         |
| Passed / Failed / Skip  | 132 / 0 / 0  | 199 / 0 / 0 | 199 / 0 / 0 | clean                  |
| Reported duration / run | 102–117 ms   | 402–411 ms  | 397–419 ms  | ≈ flat                 |
| Flakes                  | 0            | 0           | 0           | unchanged              |
| Slowest test            | 65 ms        | 367 ms      | 372 ms      | `SameKey_Serializes` stable |
| Build warnings/errors   | 0 / 0        | 0 / 0       | 0 / 0       | unchanged              |
| `dotnet format` clean   | (n/a)        | yes         | yes         | unchanged              |

No new tests landed since v2; the suite count and slowest-test profile are stable.

---

## Guardrail compliance

- Read-only sweep. **No source files modified.** No tests skipped, weakened, or deleted.
- Only artifacts written: this report and `audit-reports/v3/_detailed.txt` (verbose run log, safe to delete).

---

## Top-3 findings

1. **Suite is fully green and deterministic.** 199/199 × 3 consecutive Release runs with zero variance in outcome; duration variance ≤ 5%. No action required on test health.
2. **Slowest test remains stable and under threshold.** `PerWidgetGateTests.SameKey_Serializes` sits at 372 ms (v2: 367 ms) — by-design serialized-gate wait. Not a flake, not a slow-test flag. Flag for **perf-profiler** only if it drifts past ~1 s in future sweeps.
3. **Build + format are clean on Release.** `dotnet build -c Release` is 0W/0E and `dotnet format --verify-no-changes` exits 0. No regressions since v2 on either front.

---

## Recommendations (non-blocking)

1. No action needed on suite health — green, fast, deterministic.
2. Optional: install `dotnet-coverage` global tool and/or add a `coverlet.runsettings` if CI wants merged coverage reports with exclusions/thresholds (unchanged gap since v1).
