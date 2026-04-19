# Lint Polisher Sweep v2 — obsidian_widget

**Scope:** `C:\Users\lafia\csharp\obsidian_widget` (entire solution, `ObsidianQuickNoteWidget.slnx`)
**Mode:** READ-ONLY — no source modifications.
**Prior report:** `audit-reports\lint-polisher.md` (v1).

## Commands run

| Command                                                        | Exit | Result |
|----------------------------------------------------------------|-----:|--------|
| `dotnet build -c Release /p:TreatWarningsAsErrors=false`       | 0    | Build succeeded. **0 warnings, 0 errors.** |
| `dotnet format whitespace --verify-no-changes`                 | 0    | **Clean.** |
| `dotnet format analyzers --verify-no-changes --severity info`  | 2    | 50 info diagnostics (no errors, no warnings). |
| `dotnet format style --verify-no-changes --severity info`      | 2    | 57 info diagnostics (no errors, no warnings). |

Exit 2 on the `--severity info` runs is expected: `dotnet format` reports exit 2 whenever any finding at the requested severity exists, even if it would not fail the build. The underlying project-configured severity for every finding below is `suggestion`; none block CI.

## Verification of v1 items

| v1 finding | v2 status |
|-----------|-----------|
| **13 × WHITESPACE errors** (`NoteTemplates.cs`, `CardDataBuilderTests.cs`) blocking `--verify-no-changes` | ✅ **Resolved.** `dotnet format whitespace --verify-no-changes` exits 0. No WHITESPACE diagnostics anywhere in the repo. |
| **IDE0044** on `JsonStateStore._cache` | ⏳ **Still pending.** Now reported at `JsonStateStore.cs(33,45)` (was line 19 — file was lightly edited, analyzer target unchanged). Field remains `private Dictionary<string, WidgetState> _cache;`, single-assigned in ctor at line 39; mutated only via indexer/`Remove` inside `Save` / `Delete`. Rule is still correct; one-keyword mechanical fix remains available but has not been applied. |
| **.editorconfig suppressions** (IDE0028/0130/0290/0300/0301/0305 at `suggestion`) | ✅ **Still justified.** Config unchanged since v1. Each flagged site re-inspected this pass; every occurrence remains a purely cosmetic rewrite. No occurrence masks a defect. |
| **`#pragma warning disable SYSLIB1054`** in `Com/Ole32.cs` | ✅ **Still justified and unchanged.** Single pair, scoped to one P/Invoke, carries accurate justification (IUnknown-marshalled `object` unsupported by source-generated marshaller). Only pragma disable in the repo. |

## Diagnostic summary — v2

Unique counts after de-duplicating the test-project's re-analysis of Core files.

| Rule      | Severity (effective) | Surface           | Count (unique) | Bucket                |
|-----------|----------------------|-------------------|---------------:|------------------------|
| WHITESPACE| error                | whitespace        |              0 | — (was 13 in v1) |
| IDE0017   | info                 | style             |              1 | (a) mechanical — NEW |
| IDE0028   | info (suggestion)    | style + analyzers |             ~10 | (a) mechanical, deliberately suppressed |
| IDE0044   | info                 | style             |              1 | (a) mechanical — still pending from v1 |
| IDE0057   | info                 | style             |              4 | (a) mechanical — NEW |
| IDE0060   | info                 | style             |              1 | (b) judgment — NEW |
| IDE0290   | info (suggestion)    | style + analyzers |              4 | (b) judgment, deliberately suppressed |
| IDE0300   | info (suggestion)    | style + analyzers |             ~10 | (a) mechanical, deliberately suppressed |
| IDE0301   | info (suggestion)    | style + analyzers |              7 | (a) mechanical, deliberately suppressed |
| IDE0305   | info (suggestion)    | style + analyzers |              2 | (a) mechanical, deliberately suppressed |

No CA-series, security, or perf analyzer diagnostics.
No warning-severity findings anywhere (build is 0/0).

## New findings since v1

Four genuinely new diagnostics (not present in v1 report), surfaced by the `style` subcommand:

### IDE0017 — Object initialization can be simplified (1)

- `src\ObsidianQuickNoteWidget.Core\Concurrency\AsyncKeyedLock.cs(91,25)` — mechanical; `new X(); x.Y = …; x.Z = …;` → object initializer block. Bucket **(a)**.

### IDE0057 — Substring can be simplified (4)

- `src\ObsidianQuickNoteWidget.Core\Cli\ObsidianCli.cs(287,51)`
- `src\ObsidianQuickNoteWidget.Core\Cli\ObsidianCliParsers.cs(74,97)`
- `src\ObsidianQuickNoteWidget.Core\Cli\ObsidianCliParsers.cs(75,104)`
- `src\ObsidianQuickNoteWidget.Core\Cli\ObsidianCliParsers.cs(104,42)`

Prefer range operator (`s[i..]` / `s[i..j]`) over `s.Substring(i)` / `s.Substring(i, j-i)`. Bucket **(a)** — mechanical, semantics-preserving.

### IDE0060 — Remove unused parameter (1)

- `src\ObsidianQuickNoteWidget\Providers\ObsidianWidgetProvider.cs(319,60)` — `WidgetSession session` parameter of `HandleOpenRecentAsync` is not referenced in the method body.

Bucket **(b) judgment.** `HandleOpenRecentAsync` is almost certainly wired into a command-dispatch table whose delegate signature requires `(WidgetSession, string?)`; removing the parameter would break the dispatcher. **Do not auto-fix.** Route: **bug-hunter** to confirm the signature is forced by the dispatcher, then either (i) leave and suppress inline with reason, or (ii) use a discard parameter name.

### IDE0290 new sites (2)

New since v1 (v1 flagged 3; v2 flags 4 — net +1, one site changed):

- `src\ObsidianQuickNoteWidget.Core\Concurrency\AsyncKeyedLock.cs(17,12)` — NEW file/NEW site since v1.
- `src\ObsidianQuickNoteWidget\Providers\ObsidianWidgetProvider.cs(479,16)` — NEW site since v1.

(Previously flagged `ObsidianCli.cs:26`, `NoteCreationService.cs:17`, `ClassFactory.cs:38` — still present.)

Bucket **(b) judgment.** Deliberately held at `suggestion` in `.editorconfig`; team has not adopted primary constructors project-wide. No action.

## Findings that persist from v1 (unchanged verdicts)

- **IDE0028** collection initializer — 10 unique sites across `WidgetState.cs`, `JsonStateStore.cs`, and several test files (new sites in `JsonStateStoreTests.cs`, `PerWidgetGateTests.cs`, `AsyncSafeTests.cs` reflecting new test coverage added since v1, not a regression). All cosmetic, suppressed at `suggestion`.
- **IDE0300 / IDE0301 / IDE0305** collection-expression family — all remain cosmetic, suppressed at `suggestion`.
- **IDE0044** on `JsonStateStore._cache` — still legitimately actionable (mechanical one-keyword edit), still unfixed. See verification table above.

## Top 3 findings by impact

1. **IDE0044 on `JsonStateStore._cache`** (`JsonStateStore.cs:33`) — the *only* finding in this sweep that represents a real (minor) code-quality gap rather than a purely cosmetic preference. Field is provably single-assigned; adding `readonly` is a mechanical one-keyword change. Carried over unresolved from v1. Route: **lint-polisher** on a future write pass.
2. **IDE0060 unused parameter `session`** in `ObsidianWidgetProvider.HandleOpenRecentAsync` (line 319) — the only NEW judgment-bucket finding. Because the handler is almost certainly wired into a uniform dispatch signature, mechanical removal is unsafe. Route: **bug-hunter** to confirm the constraint; fix via discard-name or inline suppression with justification.
3. **IDE0057 × 4 `Substring` → range operator** in `ObsidianCli.cs` and `ObsidianCliParsers.cs` — net-new mechanical finding since v1. Semantics-preserving rewrite (`s.Substring(i, n)` → `s[i..(i+n)]`). Low impact, low risk; `dotnet format style` would apply it automatically. Route: **lint-polisher** on a future write pass, or leave as-is (currently only `suggestion` severity, not blocking).

## Summary

- **Whitespace verify gate: GREEN** (was RED in v1). All 13 alignment-padding errors resolved.
- **Build: 0 warnings, 0 errors** under `TreatWarningsAsErrors=false` — i.e. zero warnings suppressed by the warnings-as-errors switch either.
- **One actionable mechanical item carried over** from v1 (IDE0044 on `_cache`). Everything else at `info` is either (a) cosmetic with documented, still-valid `.editorconfig` suppression, or (b) judgment.
- **Three new rule families** surfaced this pass (IDE0017, IDE0057, IDE0060) — two are mechanical and safe; IDE0060 is a legitimate judgment call (dispatcher signature).
- **No security, perf, or correctness analyzer hits.** `.editorconfig` suppressions remain justified. Pragma suppression in `Ole32.cs` remains justified.
