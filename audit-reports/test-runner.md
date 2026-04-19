# Test-Runner Suite-Health Report

**Target:** `C:\Users\lafia\csharp\obsidian_widget`
**Date:** 2026-04-19
**Archetype:** test-runner (read-only sweep — no code edits)
**Spec:** `C:\Users\lafia\.copilot\agents\test-runner.md`

---

## TL;DR

- **132 / 132 passing**, **0 failed**, **0 skipped** (Release, `--no-build`).
- **Zero flakes** across 3 back-to-back runs.
- Suite completes in ~**100–120 ms** of test time (~2 s wall clock incl. host startup).
- **No slow tests.** Max single-test duration observed: **65 ms** (well under the 1 s flag threshold).
- Test discovery looks clean; all test files contribute Facts/Theories.
- Coverage tooling: `coverlet.collector` **is** referenced in the test csproj. `dotnet-coverage` global tool is **not installed** (minor gap).

---

## Environment state at sweep time

- `git status` → **not a git repository** (no `.git` directory present). Could not diff working tree; relied on file mtimes instead.
- Another agent (`test-quality`) was noted as possibly active. Sweep snapshot time: `2026-04-19T00:03:10-07:00`.
- **No test source files modified within the 5 minutes before the run** — file tree appears quiescent. Running was safe.
- Most recently-touched test sources (all `.cs` under `tests/`): `ObsidianCliParsersTests.cs`, `CardDataBuilderTests.cs`, `CardTemplatesTests.cs`, `NoteCreationServiceTests.cs`, `JsonStateStoreTests.cs`, `FrontmatterBuilderTests.cs`, `FolderPathValidatorTests.cs`, `DuplicateFilenameResolverTests.cs`, `FilenameSanitizerTests.cs`.

---

## Build

```
dotnet build -c Release --nologo
```

- All 4 projects built clean: **0 warnings, 0 errors**, ~1.55 s elapsed.
- Projects: `ObsidianQuickNoteWidget.Core`, `ObsidianQuickNoteWidget.Core.Tests`, `ObsidianQuickNoteTray`, `ObsidianQuickNoteWidget`.

---

## Test runs (3× back-to-back)

Command: `dotnet test -c Release --nologo --no-build`

| Run | Result  | Passed | Failed | Skipped | Total | Reported duration | Wall clock |
| --- | ------- | ------ | ------ | ------- | ----- | ----------------- | ---------- |
| 1   | Passed! | 132    | 0      | 0       | 132   | 117 ms            | 2.11 s     |
| 2   | Passed! | 132    | 0      | 0       | 132   | 102 ms            | 2.18 s     |
| 3   | Passed! | 132    | 0      | 0       | 132   | 105 ms            | 2.08 s     |

**Flake status: NONE.** All 132 tests were deterministic across all 3 runs. No ordering, timing, or state-leak instability observed.

---

## Slow-test audit (> 1 s threshold)

Ran once with `--logger:"console;verbosity=detailed"` and scanned per-test durations.

- **Tests above 1000 ms: 0.**
- **Max observed duration: 65 ms** — `JsonStateStoreTests.Delete_RemovesWidget` (54 ms) and `NoteCreationServiceTests.Create_OpenAfterCreate_OpensNote` (65 ms) were the two highest, both involving filesystem I/O. Acceptable for this repo.
- Typical unit test duration: 1–15 ms. No outliers warranting perf-profiler handoff.

---

## Test discovery

| File                                 | Classes | `[Fact]` + `[Theory]` count |
| ------------------------------------ | ------- | --------------------------- |
| `CardDataBuilderTests.cs`            | 1       | 12                          |
| `CardTemplatesTests.cs`              | 1       | 4                           |
| `DuplicateFilenameResolverTests.cs`  | 1       | 5                           |
| `FilenameSanitizerTests.cs`          | 1       | 4                           |
| `FolderPathValidatorTests.cs`        | 1       | 2                           |
| `FrontmatterBuilderTests.cs`         | 1       | 6                           |
| `JsonStateStoreTests.cs`             | 1       | 8                           |
| `NoteCreationServiceTests.cs`        | 1       | 15                          |
| `ObsidianCliParsersTests.cs`         | 1       | 16                          |
| **Totals**                           | **9**   | **72 test methods**         |

- **72 methods × theory expansion → 132 executed test cases.** Counts reconcile — no silent under-discovery.
- Every test class lives under the `ObsidianQuickNoteWidget.Core.Tests` namespace (confirmed via runner output). No orphaned classes.
- `xunit` 2.9.3 + `xunit.runner.visualstudio` 3.1.4 + `Microsoft.NET.Test.Sdk` 17.14.1 — all current and compatible.
- No `[Fact(Skip=...)]`, `[Theory(Skip=...)]`, or disabled tests found (0 skips).

---

## Coverage tooling

- ✅ `coverlet.collector` **6.0.4** referenced in `tests/ObsidianQuickNoteWidget.Core.Tests/ObsidianQuickNoteWidget.Core.Tests.csproj`. Users can run:
  ```
  dotnet test -c Release --collect:"XPlat Code Coverage"
  ```
- ⚠️ `dotnet-coverage` global tool **not installed** on this machine (`dotnet tool list -g` shows none; `dotnet-coverage` command not on PATH). Not required — coverlet is sufficient — but worth noting if the team wants Microsoft's cross-platform runtime coverage tooling for .coverage/cobertura merging in CI.
- No `coverlet.msbuild` reference — only the collector path is wired up (fine for VSTest-driven runs).

**Gap:** No repository-level coverage config file (e.g., `.runsettings`, `coverlet.runsettings`) observed. Teams typically want one to exclude generated code / set thresholds. Not a blocker.

---

## Guardrail compliance

- No test was skipped, ignored, deleted, or weakened (read-only sweep; no edits made to any source file).
- Only file written by this agent: this report (`audit-reports/test-runner.md`) and transient output logs (`test-runs.txt`, `test-detailed.txt`) in the repo root used as scratch for this sweep.

---

## Recommendations (non-blocking)

1. **No action needed on test health.** Suite is green, fast, deterministic.
2. If CI wants consolidated coverage reports, consider installing the `dotnet-coverage` global tool or adding a `coverlet.runsettings` with exclusion patterns.
3. Repository is not a git working copy on this machine — verify this was intentional; otherwise re-clone so agents coordinating via `git status` can detect mid-edit states.

---

## Artifacts

- `test-runs.txt` — summary output of the 3 back-to-back runs.
- `test-detailed.txt` — detailed per-test output (names + durations) from a single verbose run.

Both are scratch files; safe to delete.
