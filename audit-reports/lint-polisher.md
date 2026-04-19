# Lint Polisher Sweep — obsidian_widget

**Scope:** `C:\Users\lafia\csharp\obsidian_widget` (entire solution, `ObsidianQuickNoteWidget.slnx`)
**Mode:** READ-ONLY — no fixes applied.
**Command:** `dotnet format --verify-no-changes --severity info` (repo root)
**Exit code:** `2` (verify failed)

## Toolchain detected

- `.editorconfig` at repo root (explicit severities)
- `Directory.Build.props` — `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest`, `NoWarn=CA1852`
- No `.pre-commit-config.yaml`, no external linters (single-ecosystem .NET repo)

## Finding count (by rule id)

Raw counts reported by `dotnet format` (each Core-project diagnostic is re-emitted by the test project because of the `ProjectReference`, so the raw total double-counts some Core findings). **Unique counts** collapse those duplicates.

| Rule          | Severity | Raw | Unique | Bucket                |
|---------------|----------|-----|--------|------------------------|
| WHITESPACE    | error    | 13  | 13     | (a) mechanical        |
| IDE0028       | info     | 14  |  8     | (a) mechanical (cosmetic, deliberately suppressed) |
| IDE0300       | info     | 18  |  6     | (a) mechanical (cosmetic, deliberately suppressed) |
| IDE0301       | info     |  8  |  7     | (a) mechanical (cosmetic, deliberately suppressed) |
| IDE0305       | info     |  4  |  2     | (a) mechanical (cosmetic, deliberately suppressed) |
| IDE0290       | info     |  4  |  3     | (b) judgment (deliberately suppressed) |
| IDE0044       | info     |  2  |  1     | (b) judgment — legitimately mechanical, left as info |
| **TOTAL**     |          | **63** | **40** |  |

## Findings by category

### (a) Auto-mechanical — would be fixed by `dotnet format` if allowed to write

#### WHITESPACE (13, hard errors — blocking `--verify-no-changes`)

All are intentional alignment padding that the formatter disagrees with. No behavioural impact.

- `src\ObsidianQuickNoteWidget.Core\Notes\NoteTemplates.cs` — lines 16, 17, 19, 20, 26, 28, 29
  → extra spaces aligning `=>` arms of two `switch` expressions (`Blank   =>`, `Daily   =>`, etc.).
- `tests\ObsidianQuickNoteWidget.Core.Tests\CardDataBuilderTests.cs` — lines 15, 15, 64, 65, 141, 169
  → line 15 has two object-initializer assignments on one line (formatter wants them split); lines 64–65, 141, 169 are alignment padding inside `Assert.Equal("D",    titles[3])`-style calls.

**Note:** These are the *only* findings that actually fail the verify gate. Everything below is `info` and does not fail the build (they only surface under `--severity info`).

#### IDE0028 / IDE0300 / IDE0301 / IDE0305 — collection expression / initializer suggestions

Unique locations:

- IDE0028 (collection initializer):
  - `src\...Core\State\JsonStateStore.cs` lines 65, 68, 72
  - `src\...Core\State\WidgetState.cs` lines 14, 15, 16, 17
  - `tests\...Core.Tests\NoteCreationServiceTests.cs` line 12
- IDE0300 (collection expression — array literal):
  - `src\...Core\Notes\FilenameSanitizer.cs` line 8
  - `src\...Core\Notes\FolderPathValidator.cs` line 17
  - `src\...Core\Notes\NoteTemplates.cs` lines 26, 27, 28, 29
- IDE0301 (collection expression — empty):
  - `src\...Core\Cli\ObsidianCli.cs` line 118
  - `src\...Core\Cli\ObsidianCliParsers.cs` line 36
  - `src\...Core\Notes\FrontmatterBuilder.cs` line 41
  - `src\...Core\Notes\NoteTemplates.cs` line 30
  - `tests\...Core.Tests\FrontmatterBuilderTests.cs` lines 14, 31
  - `tests\...Core.Tests\NoteCreationServiceTests.cs` line 31
- IDE0305 (collection expression — fluent `.ToArray()` / `.ToList()`):
  - `src\...Core\Cli\ObsidianCliParsers.cs` line 44
  - `src\...Core\Notes\FrontmatterBuilder.cs` line 46

All four rules are **deliberately suppressed at `suggestion`** in `.editorconfig` (see cross-check below). They are genuinely cosmetic in this codebase — spot-check confirms every flagged site is a straightforward `new[] { ... }` / `new List<T> { ... }` / `Array.Empty<T>()` / `.ToArray()` pattern with no semantic subtlety.

#### IDE0044 — Make field readonly (1)

- `src\...Core\State\JsonStateStore.cs` line 19 — `private Dictionary<string, WidgetState> _cache;`

Verified: `_cache` is assigned only once, in the constructor (line 25). `Save` / `Delete` mutate the dictionary via indexer/`Remove`, never reassign. Rule is correct; field *can* be `readonly`. This is mechanical (one-keyword edit), not judgment, despite landing in the "judgment" rule family. **Not currently enforced** (info severity, not configured in `.editorconfig`). No action under READ-ONLY.

### (b) Judgment-call — defer to humans / sibling agents

#### IDE0290 — Use primary constructor (3 unique)

- `src\...Core\Cli\ObsidianCli.cs` line 26
- `src\...Core\Notes\NoteCreationService.cs` line 17
- `src\...Core\Com\ClassFactory.cs` line 38 *(widget host, not Core)*

Deliberately left at `suggestion` in `.editorconfig`. Primary constructors change declaration shape and XML-doc placement; team has chosen not to adopt them project-wide. Legitimate judgment call. **Do not convert mechanically.** If revisited, route to `refactorer`.

### (c) Deliberately suppressed in `.editorconfig` — verified legitimate

`.editorconfig` sets the following at `suggestion` (i.e. surfaced under `--severity info`, not failing the build):

| Rule     | Purpose                                     | Verdict                                                                                |
|----------|---------------------------------------------|----------------------------------------------------------------------------------------|
| IDE0028  | Use collection initializers                 | ✅ Cosmetic in every flagged site. Legitimate.                                         |
| IDE0130  | Namespace does not match folder             | ✅ No current findings. Pre-emptive config for folder/namespace drift. Legitimate.     |
| IDE0290  | Use primary constructor                     | ✅ Style preference, not a defect. Legitimate.                                         |
| IDE0300  | Collection expression: array                | ✅ Cosmetic in every flagged site. Legitimate.                                         |
| IDE0301  | Collection expression: empty                | ✅ Cosmetic in every flagged site. Legitimate.                                         |
| IDE0305  | Collection expression: fluent               | ✅ Cosmetic in every flagged site. Legitimate.                                         |

**None of these hide a real defect.** Each flagged occurrence is purely a style rewrite.

## Cross-check: `#pragma warning disable` in source

Exactly **one** pragma pair in the repo:

- `src\ObsidianQuickNoteWidget\Com\Ole32.cs` lines 14–22

```csharp
// NOTE: Not convertible to [LibraryImport] — the IUnknown-marshalled `object`
// parameter is not supported by the source-generated marshaller. Keep DllImport.
#pragma warning disable SYSLIB1054
[DllImport("ole32.dll")]
public static extern int CoRegisterClassObject(
    ref Guid rclsid,
    [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
    uint dwClsContext,
    uint flags,
    out uint lpdwRegister);
#pragma warning restore SYSLIB1054
```

**Verdict: valid.** Scoped to a single P/Invoke, carries a written justification, and the justification is technically accurate — the `LibraryImport` source generator does not support `UnmanagedType.IUnknown` marshalling of `object`. `.editorconfig` also enforces `SYSLIB1054 = warning` globally (i.e. elevated to error under `TreatWarningsAsErrors`), which makes this targeted suppression necessary. No other `#pragma warning disable` exists in the repo.

## Top 3 findings by impact

1. **13 × WHITESPACE errors** in `NoteTemplates.cs` and `CardDataBuilderTests.cs` — these are the **only** diagnostics that break `dotnet format --verify-no-changes` and would block a CI lint gate. All are intentional `=>`/column-alignment padding; `dotnet format` would remove them mechanically. Route: **lint-polisher** (apply `dotnet format --include` scoped to these two files) or opt out alignment-preserving rules if the alignment is wanted.
2. **IDE0044 on `JsonStateStore._cache`** (line 19) — field is provably single-assigned; adding `readonly` is a one-keyword mechanical fix and the only finding in this sweep that flags a real (minor) code-quality gap rather than pure cosmetic preference. Route: **lint-polisher** on a future write pass.
3. **IDE0290 × 3 (primary-constructor suggestions)** in `ObsidianCli.cs`, `NoteCreationService.cs`, `ClassFactory.cs` — legitimately-suppressed judgment call; flagged here only to confirm the suppression in `.editorconfig` is not masking a defect. No action. Route if reopened: **refactorer**.

## Summary

- **40 unique findings** (63 raw, inflated by the test project re-analysing Core files).
- **13 block the verify gate** (all WHITESPACE alignment), **27 are info-only** and deliberately suppressed at `suggestion`.
- **1 `#pragma warning disable`** in source — justified and narrowly scoped.
- **.editorconfig suppressions are all legitimate cosmetic choices** — none hides a defect.
- No security/perf analyzer hits; no behavioural defects surfaced by the linter.
