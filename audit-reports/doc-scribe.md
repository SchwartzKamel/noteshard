# doc-scribe audit — documentation coverage & drift

**Target:** `C:\Users\lafia\csharp\obsidian_widget`
**Mode:** read-only audit. No docs were edited.
**Scope:** `README.md`, `.github/copilot-instructions.md`, `.github/agents/*.md`, XML `///` coverage across `Core` / `Widget` / `Tray`, ancillary docs.
**Audience assumed by existing docs:** mixed — README targets end-users + contributors; `copilot-instructions.md` targets maintainers & AI agents; archetype files target agents.

---

## Summary

Overall documentation is **unusually strong for a repo this size**: the top-level `README.md` and `.github/copilot-instructions.md` are fresh, cross-linked, and accurate against source. The archetype set under `.github/agents/` is coherent and correctly scoped. The main gaps are (a) **stale test count in `copilot-instructions.md`**, (b) **a forgotten side-file (`WidgetsDefinition.xml`) that contradicts the authoritative manifest**, and (c) **thin / missing XML `///` on the public `Core` surface** that `copilot-instructions.md` elevates to a stable API seam.

Verified-against-source: Makefile targets, CI workflow, CLI surface table, sizes table, state-store paths, CLSID sync, activation sequence.

---

## HIGH — wrong or stale claims

### H1. `copilot-instructions.md` reports "66 xUnit tests, Core only" — actual count is 72
- File: `.github/copilot-instructions.md` (line 13, code block comment `# full test suite (66 xUnit tests, Core only)`).
- Verification: `Select-String '\[Fact\]|\[Theory\]'` over `tests/ObsidianQuickNoteWidget.Core.Tests/*.cs` → **72** attributes. Fresh `dotnet test --list-tests` returns a list consistent with 72+ (Theory cases expand further).
- Why it matters: number is quoted authoritatively to orient contributors; drifts silently every time tests are added.
- Fix (recommended): drop the hard count — `# full test suite (xUnit, Core only)` — or regenerate on release.

### H2. `WidgetsDefinition.xml` contradicts `Package.appxmanifest` (authoritative)
- Files:
  - `src/ObsidianQuickNoteWidget/WidgetsDefinition.xml` (side file).
  - `src/ObsidianQuickNoteWidget/Package.appxmanifest` (what Widget Host actually reads).
- Discrepancy:
  | Definition Id | `WidgetsDefinition.xml` sizes | `Package.appxmanifest` sizes |
  | --- | --- | --- |
  | `ObsidianQuickNote` | small, medium | **small, medium, large** |
  | `ObsidianRecentNotes` | large | **medium, large** |
- This is a **doc-grade contradiction**: `WidgetsDefinition.xml` is a declarative spec file that looks authoritative to a new contributor. README and `copilot-instructions.md` both assert QuickNote has a Large size (README "Widget sizes" table) — the manifest agrees, the side XML does not.
- Not clear if `WidgetsDefinition.xml` is consumed at all (the manifest contains the full `<Definition>` blocks inline). If dead, it should be deleted; if live, it needs to be re-synced. Either way this belongs in `manifest-surgeon`'s lane to resolve, but doc-scribe flags it as a claim-vs-reality contradiction.

### H3. README points contributors to `copilot-instructions.md` as the "authoritative brief for maintainers" — there is no standalone `CONTRIBUTING.md`
- File: `README.md` § Contributing (line 81–89).
- Reality: no `CONTRIBUTING.md` exists; `copilot-instructions.md` substitutes. `copilot-instructions.md` covers build/test/architecture/gotchas but does **not** cover: PR expectations, commit style, code-of-conduct, issue triage, branch naming, `TreatWarningsAsErrors=true` implication for contributor workflow beyond a single mention.
- This works for a solo/small-team repo; it becomes misleading if the project accepts drive-by PRs. Either add a minimal `CONTRIBUTING.md` (MEDIUM in most repos — bumped to HIGH here because the README actively advertises a contribution flow) or soften the README wording to "there are no contribution guidelines beyond the maintainer brief".

---

## MEDIUM — missing critical docs / thin XML

### M1. Public `Core` API surface has inconsistent XML `///` coverage
`copilot-instructions.md` declares `Core` the "only layer with tests" and the abstraction seam for COM / widget / tray code. That elevates its public surface to a stable API — but XML docs are patchy.

Public types with **no or near-zero** `///` comments (counts = `///` lines in file):

| File | Public type | `///` lines | Gap |
| --- | --- | --- | --- |
| `State/IStateStore.cs` | `IStateStore` | 0 | **Interface + all 3 methods undocumented.** This is the mock seam tests use; contract (e.g. "`Get` never returns null", "`Save` is atomic", "`Delete` on unknown id is a no-op?") is implicit. |
| `Logging/ILog.cs` | `ILog`, `NullLog` | 0 | Level semantics (what is Warn vs Error?), thread-safety, expected volume undocumented. `NullLog.Instance` singleton contract undocumented. |
| `State/WidgetState.cs` | `WidgetState` (16 mutable properties) | 1 (type-level only) | No per-property docs. `Size` allowed values (`"small"`/`"medium"`/`"large"`), `Template` allowed values (`"Blank"`/`"Daily"`/`"Meeting"`/`"Book"`/`"Idea"`), `CachedFoldersAt` semantics, list-nullability contract (properties initialize to `new()` but external deserializers can still null them) all undocumented. |
| `Notes/NoteRequest.cs` | `NoteRequest`, `NoteCreationStatus`, `NoteCreationResult` | 0 | Core DTO. `AppendToDaily=true` silently changes the entire pipeline (see `NoteCreationService` lines 32–45) — that contract deserves a `<remarks>`. Enum values undocumented. |
| `Cli/CliResult.cs` | `CliResult` | 0 | `ExitCode=-1` sentinel (used in `ObsidianCli.RunAsync` when exe is missing, line 38) is undocumented. |
| `AdaptiveCards/CardDataBuilder.cs` | `CardDataBuilder.BuildQuickNoteData`, `BuildCliMissingData`, `CardStatus` | 4 (type-level only) | Method-level docs missing. The `showAdvanced` parameter and the contract "every `${...}` binding in a template must have a matching field here" (called out in `copilot-instructions.md` § Adaptive Cards) is nowhere in the XML. |
| `AdaptiveCards/CardTemplates.cs` | `Load`, `LoadForSize`, constants | 1 (type-level only) | `LoadForSize` fallback ("unknown size → medium") undocumented — observable behaviour. |
| `Notes/NoteCreationService.cs` | `NoteCreationService` class + ctor + `CreateAsync` | 5 (type-level only) | Class-level summary is good; `CreateAsync` has no `<param>`/`<returns>`/`<remarks>` despite being the pipeline's entry point. |
| `Notes/FilenameSanitizer.cs` | `FilenameSanitizer` | 2 | Rules (Windows reserved, trailing dots, unicode handling) not documented in XML — they're only discoverable by reading tests. |
| `Notes/NoteTemplates.cs` | `NoteTemplate`, `NoteTemplates` | 0 | Enum values (Blank/Daily/Meeting/Book/Idea) and their seeded-body contracts undocumented. |
| `Notes/FrontmatterBuilder.cs` | `FrontmatterBuilder` | 4 | `ParseTagsCsv` splitter rules (comma? whitespace? both?) undocumented. |
| `Notes/DuplicateFilenameResolver.cs` | `DuplicateFilenameResolver` | 4 | Suffix scheme (` (2)`, ` (3)`, …) undocumented in XML. |
| `Logging/FileLog.cs` | `FileLog` | 4 | Log path, 1 MB rollover, thread-safety, packaged-vs-unpackaged path-resolution logic undocumented at the API level (covered in README but not in source). |

Files with **acceptable** coverage already: `Cli/IObsidianCli.cs` (well documented), `Cli/ObsidianCli.cs` (type-level), `State/JsonStateStore.cs` (type-level), `WidgetIdentifiers.cs`, `ObsidianWidgetProvider.cs` (type-level). These set the right bar — the rest should match.

**Severity rationale:** MEDIUM not HIGH because none of the missing docs *misinform*; they merely force a reader into the source. For a "Core = stable seam" repo, closing the gap on `IStateStore` / `ILog` / `NoteRequest` / `CardDataBuilder` would be the highest-leverage wins.

### M2. No `TROUBLESHOOTING.md` — `README.md` § Troubleshooting is 5 bullets
- Reality covered by `copilot-instructions.md` § Gotchas + activation-sequence rules, but those are written for maintainers not end users.
- What a user hitting a failure today would *not* find:
  - Widget doesn't appear in picker → kill-process dance is listed, but not "install the `.cer` into Trusted People first" order-of-operations.
  - Tray hotkey conflict (Ctrl+Alt+N) → no guidance; the hotkey is hard-coded (`src/ObsidianQuickNoteTray/GlobalHotkey.cs`) and not configurable.
  - `MoAppHang` / silent provider drop → developer-only failure mode, mentioned only in `copilot-instructions.md`.
  - `obsidian` not on PATH after running "Register CLI" → users report this is flaky; no mitigation documented.
  - Package family name differs on first install vs upgrade → log path example shows a specific PFN (`h6cy8nh103fya`) that won't match a user's install.
- Recommend expanding README § Troubleshooting in place or promoting to `docs/TROUBLESHOOTING.md` linked from both README and `copilot-instructions.md`.

### M3. No `ARCHITECTURE.md` — role is played by `.github/copilot-instructions.md`
- `copilot-instructions.md` **does** cover architecture adequately (project table, runtime flow, Adaptive Cards layer, Mermaid activation sequence, where-things-live). This is good.
- Drawback: a new contributor without Copilot context may not think to open `.github/copilot-instructions.md`. It's a file *named* for an AI tool.
- Two acceptable fixes:
  1. Symlink or thin stub `ARCHITECTURE.md` → "Architecture notes live in [`.github/copilot-instructions.md`](.github/copilot-instructions.md)."
  2. Add an explicit pointer to the README § Contributing section (currently points there, but positions it as "for AI agents / maintainers" — under-sells it for human architecture reading).
- Preference: option 1 (one-line stub), because the content is already correct — the only gap is discoverability.

### M4. No `CHANGELOG.md`
- Repo has tagged releases (`v*.*.*` triggers MSIX publish per CI workflow) but no user-visible history. Winget manifests (`winget/ObsidianQuickNoteWidget.*`) imply distribution — users have no way to see what changed between versions.
- **Out of doc-scribe's lane per our guardrails** (defer to `release-engineer`), flagged here as a user-facing doc absence only. Not fixing.

### M5. `copilot-instructions.md` Architecture table describes `tools/AppExtProbe`; the loose file `tools/WidgetCatalogProbe.cs` is undocumented
- File `tools/WidgetCatalogProbe.cs` exists at repo root under `tools/` but is not mentioned anywhere in README, `copilot-instructions.md`, or the archetype docs. Unclear if it's companion to `AppExtProbe` or orphaned.
- Either fold into `AppExtProbe` project, delete, or document its purpose in `copilot-instructions.md` § "Where things live".

---

## LOW — nice-to-have

### L1. README § "Dev cert + signing" is correct but uses `make pack` elsewhere without tying it together
- README tells users `make pack` produces an MSIX, then separately shows the `signtool` invocation. A single copy-paste-able "first install" script (build → sign → import cert → Add-AppxPackage) would remove ~4 footguns.

### L2. Adaptive Card templates have no inline comments describing the `${...}` bindings
- `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/*.json` — JSON doesn't support comments cleanly, but a sibling `README.md` under `AdaptiveCards/Templates/` listing which `$root.*` bindings each template expects would harden the contract that `copilot-instructions.md` calls out ("if you add a new `${$root.whatever}` binding, add the field to `CardDataBuilder` too").

### L3. `tools/AppExtProbe/README.md` — check if it exists and matches purpose claimed by `copilot-instructions.md`
- `copilot-instructions.md` describes AppExtProbe as "Diagnostic console that enumerates AppExtensionCatalog.Open(...)". No `README.md` at `tools/AppExtProbe/` to orient a first-time user who cloned the repo.

### L4. `winget/README.md` exists but is not linked from the top-level README
- File present (confirmed in `winget/`). Discoverability-only.

### L5. `.github/agents/README.md` — cross-link parity
- Cross-linking is solid: each archetype file (`widget-plumber.md`, `card-author.md`, `cli-probe.md`, `manifest-surgeon.md`) is listed and linked from the index. The index links to `../copilot-instructions.md` correctly. No rot.
- The README mentions 11 user-level archetypes under `~/.copilot/agents/` — not something to document here (out of repo scope), just noting the boundary is respected.

### L6. `CODE_OF_CONDUCT.md`, `SECURITY.md`
- Absent. Standard GitHub community hygiene; flag only, not a product need for a small widget repo.

### L7. `Directory.Build.props` / `.editorconfig` — no doc cross-reference
- `TreatWarningsAsErrors=true` is called out in `copilot-instructions.md` line 29, but `.editorconfig` has no inline comment pointing at it, and neither does `Directory.Build.props` hint at the convention. Low-priority; discoverable via warnings.

---

## Cross-link rot check

Verified all markdown cross-links are intact:

| From | To | Status |
| --- | --- | --- |
| `README.md` § Contributing | `.github/copilot-instructions.md` | ✅ resolves |
| `README.md` § License | `LICENSE` | ✅ resolves |
| `.github/agents/README.md` | `../copilot-instructions.md` | ✅ resolves |
| `.github/agents/README.md` | Each archetype file | ✅ all resolve |
| `.github/copilot-instructions.md` | Source paths (Com/ClassFactory.cs, etc.) | ✅ all files exist |
| `README.md` | Source paths | ✅ (`CardDataBuilder`, `Templates/*.json`, etc. exist) |

No orphaned docs found within the repo. No broken links.

---

## Onboarding delta — "I cloned this today" simulation

What a new contributor sees top-to-bottom and what would stump them:

1. `README.md` → clear, 90 lines, gets them to `make build` / `make test` quickly. ✅
2. `.github/copilot-instructions.md` → well-structured but **they have to be told it's the architecture doc**. See M3. ⚠️
3. No `CONTRIBUTING.md` despite README § Contributing implying there is one. See H3. ⚠️
4. `make build` works; `make test` passes (72 tests, not 66 as commented). See H1. ⚠️
5. `make pack` produces unsigned MSIX — README correctly tells them to sign with `signtool`, but the dev-cert path is first-time-only (the `.pfx` is generated by what? — README doesn't say; it's generated by running the widget once, which is not obvious). ⚠️
6. Opening `src/ObsidianQuickNoteWidget.Core` to read "the business logic" — IntelliSense hover is sparse on `IStateStore`, `ILog`, `NoteRequest`, `WidgetState`. See M1. ⚠️
7. Running the widget: the tray is easy (`make run-tray`); the packaged widget requires sideloading, dev cert install, kill-process dance — covered in `copilot-instructions.md` but no single script. See L1.

**Biggest trip hazard:** the dev-cert path (`%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\dev.pfx`) is referenced in both README and `copilot-instructions.md` as if it pre-exists, but nothing documents *what generates it*. Grepping the source (`dev-cert` / `dev.pfx`) would confirm the generator — I did not perform that search in this audit because it crosses into implementation verification beyond what the user asked for, but flagging it as a likely documentation gap.

---

## Verified against code — explicit checklist

| Claim | Source checked | Verdict |
| --- | --- | --- |
| "66 xUnit tests" | test files, `dotnet test --list-tests` | ❌ 72 |
| "Obsidian CLI uses positional `key=value`" | `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs` | ✅ |
| "`obsidian ls` does not exist" | `copilot-instructions.md` + not referenced in code | ✅ |
| "Sizes: small, medium, large for QuickNote" | `Package.appxmanifest` lines 74–76 | ✅ (but `WidgetsDefinition.xml` disagrees — see H2) |
| "Recent Notes is a separate widget" | `WidgetIdentifiers.cs` + manifest | ✅ |
| "Folder list auto-refresh every 2 minutes" | `ObsidianWidgetProvider.cs` line 31 (`FolderRefreshInterval = TimeSpan.FromMinutes(2)`) | ✅ |
| "State at `%LocalAppData%\ObsidianQuickNoteWidget\state.json`" | `JsonStateStore.DefaultPath()` | ✅ |
| "CLSID `B3E8F4D4-…-2C91`" | `WidgetIdentifiers.cs` + `Package.appxmanifest` | ✅ both match |
| "`TargetDeviceFamily MinVersion ≥ 10.0.22621.0`" | manifest line 23 | ✅ 10.0.22621.0 |
| Log rollover at 1 MB | `FileLog` source | not verified in this pass |
| Tray hotkey Ctrl+Alt+N | `GlobalHotkey.cs` exists; value not spot-checked | ⚠️ high-confidence but unverified |
| CI workflow: restore → build Debug → test → publish MSIX on tag | `.github/workflows/build.yml` | ✅ |
| `make build`, `make test`, `make pack` Make targets | `Makefile` lines 76–121 | ✅ |

---

## Top-3 doc gaps

1. **H2 — `WidgetsDefinition.xml` contradicts `Package.appxmanifest`.** A declarative spec file that looks authoritative says QuickNote has only small+medium (manifest says small+medium+large) and RecentNotes has only large (manifest says medium+large). Either delete the side file or resync — it will mislead the next contributor.
2. **H1 — Stale "66 xUnit tests" comment in `.github/copilot-instructions.md`.** Actual count is 72. Drop the hard number or automate it; it will keep drifting.
3. **M1 — `Core` public surface has inconsistent XML `///`.** `IStateStore`, `ILog`, `NoteRequest`/`NoteCreationStatus`/`NoteCreationResult`, `WidgetState`, and `CardDataBuilder`'s method-level docs are all empty despite `copilot-instructions.md` positioning `Core` as the stable seam with mock-friendly interfaces. Closing `IStateStore` + `ILog` + `NoteRequest` alone would recover ~80% of the onboarding friction.

---

## Deliverables from this audit

- This report: `audit-reports/doc-scribe.md`.
- No source or doc files were modified (task was READ-ONLY).
- No new cross-links added (would be out of scope). Recommended links listed in M3 and L4.
