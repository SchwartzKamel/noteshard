# Test-Author Coverage Audit — Obsidian Quick Note Widget

**Scope:** `src/ObsidianQuickNoteWidget.Core`, `src/ObsidianQuickNoteWidget`, `src/ObsidianQuickNoteTray`  
**Test project:** `tests/ObsidianQuickNoteWidget.Core.Tests` (xUnit, net10.0, coverlet). `InternalsVisibleTo` is configured for the Core test project.  
**Mandate:** Read-only. Identify gaps only. A parallel agent (`test-quality`) owns the write side.

---

## 1. Per-file coverage map

### Core

| File | Test file | Coverage state |
|---|---|---|
| `AdaptiveCards/CardDataBuilder.cs` | `CardDataBuilderTests.cs` | **Strong.** Folder bucket ordering, dedup, recents cap, status precedence, `showAdvanced` flip, CLI-missing payload all asserted structurally via `JsonDocument`. |
| `AdaptiveCards/CardTemplates.cs` | `CardTemplatesTests.cs` | **Strong.** Per-size routing uses unique markers (mutation-resistant); fallback branch and template JSON shape asserted. |
| `Cli/CliResult.cs` | — | **Indirect only.** `Succeeded` getter (`ExitCode == 0`) is never asserted directly; only relied on through fakes. |
| `Cli/IObsidianCli.cs` | (interface) | n/a |
| `Cli/ObsidianCli.cs` | — | **None.** Process-based, genuinely hard. `IsAvailable` false-path, `RunAsync` exe-not-found, `CreateNoteAsync` body-empty branch (skips `content=` arg), `OpenNoteAsync` empty-path vault-open branch, timeout kill path all uncovered. |
| `Cli/ObsidianCliParsers.cs` | `ObsidianCliParsersTests.cs` | **Strong.** All three helpers covered, including escape-ordering regression guard and CRLF handling. |
| `Logging/FileLog.cs` | — | **None.** Roll-at-1MB boundary, silent-swallow-on-IO-exception contract, concurrent writer safety uncovered. |
| `Logging/ILog.cs` + `NullLog` | — | Trivial; `NullLog.Instance` exercised transitively. |
| `Notes/DuplicateFilenameResolver.cs` | `DuplicateFilenameResolverTests.cs` | **Mostly covered.** Gaps: `MaxAttempts=1000` exhaustion → timestamp fallback branch; null-extension handled inline but not isolated; null `exists` probe (guard clause) uncovered. |
| `Notes/FilenameSanitizer.cs` | `FilenameSanitizerTests.cs` | **Mostly covered.** Gaps: `COM0`/`LPT0` should NOT be reserved (current code excludes digit '0') — negative assertion missing; exact length-120 boundary; post-truncation trim-of-trailing-dot double-pass. |
| `Notes/FolderPathValidator.cs` | `FolderPathValidatorTests.cs` | **Mostly covered.** Gaps: bare `/` → `Ok("")`; `a//b` → empty-segment fail; two-char drive-letter boundary `X:` (len==2, triggers absolute-path rejection); leading-space segment `" foo"` (currently tested only trailing). |
| `Notes/FrontmatterBuilder.cs` | `FrontmatterBuilderTests.cs` | **Mostly covered.** Gaps: date-only (no tags) emits `created:` with no `tags:` line; tags-only (no date) symmetric; `YamlQuote` triggers for each of `#,[]{}&*!|>'"%@` individually (only `:` tested); **empty-string tag** forced-quote branch; null `body` arg (currently tested only with non-null). |
| `Notes/NoteCreationService.cs` | `NoteCreationServiceTests.cs` | **Strong overall.** Gaps: template-body composition join (`seeded.TrimEnd() + "\n\n" + user` — ordering/separator); case-insensitive merge of template tag with user-supplied CSV (e.g. `TagsCsv="Meeting"` + `Template=Meeting` must yield one `meeting`); `CancellationToken` propagation to fake CLI; `OpenAfterCreate` suppressed when creation fails. |
| `Notes/NoteRequest.cs` | — | Record; no behavior. |
| `Notes/NoteTemplates.cs` | — | **Thin.** Only `Meeting` is exercised indirectly by `Create_TemplateSeedsBody`. `Blank`, `Daily`, `Book`, `Idea`, and the `default` fallback branch are uncovered; a swapped-switch-arm mutation is undetectable today. |
| `State/IStateStore.cs` | (interface) | n/a |
| `State/JsonStateStore.cs` | `JsonStateStoreTests.cs` | **Strong.** Load/Save/Delete round-trip, clone-on-read, corrupt-file safety, missing-dir creation. Gap: atomic `tmp→move` replace guarantee (old file survives a write that fails mid-serialization) and `Get` on empty widgetId argument-guard. |
| `State/WidgetState.cs` | — | POCO; exercised transitively. |

### Widget (`src/ObsidianQuickNoteWidget`) — **zero tests**

| File | Testability |
|---|---|
| `Providers/ObsidianWidgetProvider.cs` | Mixed. COM lifecycle methods (`CreateWidget`, `Activate`, `PushUpdate`) are entangled with `WidgetManager.GetDefault()`, which can't be stubbed. But the static/pure helpers `ParseInputs`, `ParseBool`, `RememberRecent` and the input-merging logic of `CreateNoteAsync` are testable given `InternalsVisibleTo` is already set on Core but **not yet on the Widget assembly**. |
| `Com/ClassFactory.cs`, `Com/Ole32.cs` | COM plumbing; unit-untestable. |
| `Program.cs` | Process entry point. |
| `WidgetIdentifiers.cs` | Constants. |

**Minimum viable test surface for widget:** add a new xUnit project `tests/ObsidianQuickNoteWidget.Tests` (or fold into Core.Tests), add `[assembly: InternalsVisibleTo(...)]` to the Widget assembly, and cover the three static helpers plus the extracted `CreateNoteAsync` input-merging / state-mutation logic. This alone would take widget-project coverage from 0% to ≈ the branch count of `ParseInputs`/`ParseBool`/`RememberRecent`/`CreateNoteAsync`'s post-result state updates — the highest-risk pure code in that project.

### Tray (`src/ObsidianQuickNoteTray`) — **zero tests**

| File | Testability |
|---|---|
| `QuickNoteForm.cs` | WinForms; unit-untestable without UI automation. `LoadState`/`SaveState` are the one pure-ish seam but they mutate `_store` via controls — would require extraction to a controller class first. |
| `GlobalHotkey.cs` | P/Invoke; untestable. |
| `Program.cs` | Entry point. |

**Minimum viable test surface for tray:** none without a refactor. Flag for future extraction of a `QuickNoteController` seam; out of scope for a pure test-author sweep.

---

## 2. Gap ranking (risk × exposure)

| # | Gap | Risk | Exposure | Priority |
|---|---|---|---|---|
| 1 | `ObsidianWidgetProvider.ParseInputs` — user-controlled JSON from widget action data | High (silent catch, type confusion across string/bool/number) | Hit on every `createNote` verb | **P0** |
| 2 | `ObsidianWidgetProvider.RememberRecent` — LRU list for recents dropdown | High (ordering/dedup bug visible in UI on every note) | Hit on every successful create | **P0** |
| 3 | `NoteCreationService.BuildBody` — seeded-template + user-body join | High (newline collapse produces mangled notes silently) | Hit on every templated note | **P0** |
| 4 | `NoteTemplates.SeedBody` — 4-of-5 enum arms + default unreached | Medium (wrong template body silently served) | Hit on every templated note | **P1** |
| 5 | `NoteCreationService` template-tag + user-tag case-insensitive merge | Medium (duplicate tags in frontmatter; `OrdinalIgnoreCase → Ordinal` mutation survives today) | Hit on every templated note | **P1** |
| 6 | `FolderPathValidator` — bare `/`, `a//b` empty-segment, `X:` 2-char drive boundary | Medium (vault-escape invariant) | Hit on every create | **P1** |
| 7 | `ObsidianWidgetProvider.ParseBool` — null/unknown fallback contract | Medium (stuck toggles) | Hit on every create | **P1** |
| 8 | `FrontmatterBuilder.YamlQuote` — per-trigger char theory + empty-tag forced-quote | Low–Medium (YAML parse breakage for tags containing `#,[]{}...`) | Hit when users type special-char tags | **P2** |
| 9 | `FilenameSanitizer` — `COM0`/`LPT0` negative reservation, exact-120 boundary | Low | Rare user input | **P3** |
| 10 | `FileLog.Roll` — 1MB rollover boundary | Low (disk growth, not behaviorally visible) | Slow accumulation | **P3** |

---

## 3. Proposed new tests — behavioral contracts + DDM mutation kill

Each proposal lists: **SUT** · **Contract** · **Parameterization** · **Assertion shape** · **Mutation(s) the test kills**. No code is provided — `test-quality` owns authoring.

### P0-1 · `ObsidianWidgetProvider.ParseInputs`
- **Contract:** Given a widget `actionData` JSON payload, the parser returns a case-insensitive `Dictionary<string,string>`; string properties map verbatim, boolean properties serialize to the literals `"true"`/`"false"`, numeric/null/nested values flow through `.ToString()`, and any malformed input (null, whitespace, non-object, unparseable) yields an empty map without throwing.
- **Parameterize:** `[Theory]` with rows: `null` → empty; `""` → empty; `"not json"` → empty; `"[1,2]"` (array root) → empty; `"""{"a":"x"}"""` → `{a: "x"}`; `"""{"b":true}"""` → `{b: "true"}`; `"""{"b":false}"""` → `{b: "false"}`; `"""{"n":42}"""` → `{n: "42"}`; `"""{"TITLE":"A"}"""` queried by `"title"` → `"A"` (case-insensitive).
- **Assertions:** On the returned dictionary — `Count`, `TryGetValue`, and values by key. No substring matching on serialized output.
- **Mutations killed:**
  - `JsonValueKind.True or JsonValueKind.False` arm returning the wrong literal (e.g., both `"true"`) — caught by the `b:false → "false"` row.
  - Dropping the `try/catch` around `JsonDocument.Parse` — caught by the `"not json"` row (would throw instead of returning empty).
  - `StringComparer.OrdinalIgnoreCase` → `Ordinal` — caught by the `"TITLE"`→`"title"` lookup row.
  - Removing the `ValueKind != Object` guard — caught by the `[1,2]` array-root row (would throw on `EnumerateObject`).
- **Requires:** adding `[InternalsVisibleTo("ObsidianQuickNoteWidget.Tests")]` (or equivalent) to the Widget assembly, or hosting the helper in a Core-side testable class.

### P0-2 · `ObsidianWidgetProvider.RememberRecent`
- **Contract:** Inserts `entry` at index 0; if `entry` was already present (ordinal case-insensitive), its prior occurrence is removed first; when resulting count exceeds `max`, the tail is trimmed to exactly `max` items.
- **Parameterize:** `[Theory]` with (initial list, entry, max, expected list). Cases: empty + "A"/max=3 → `[A]`; `[B,A]` + "A"/max=3 → `[A,B]` (no duplicate, promoted); `[A,B,C]` + "D"/max=3 → `[D,A,B]` (eviction); `[A,B,C]` + "a"/max=3 → `[a,B,C]` (case-insensitive dedup replaces); single-item list + same entry/max=1 → single-item list preserved.
- **Assertions:** Exact sequence equality (`Assert.Equal(expected, list)`), plus `list.Count == Math.Min(max, expected.Count)`.
- **Mutations killed:**
  - `list.Insert(0, entry)` → `list.Add(entry)` — caught by every reorder row.
  - `RemoveAll(StringComparison.OrdinalIgnoreCase)` → `Ordinal` — caught by the `"a"` vs `"A"` row.
  - `if (list.Count > max)` → `>=` — caught by the "single-item/max=1 preserved" row (off-by-one would empty the list).
  - `RemoveRange(max, …)` → `RemoveRange(0, …)` — caught by the 3-item eviction row (would drop the newest instead of oldest).

### P0-3 · `NoteCreationService` template-body composition
- **Contract:** When `Template != Blank` and `req.Body` is non-empty, the CLI receives a body of the form `{seeded.TrimEnd()}\n\n{user}` wrapped in frontmatter — i.e., exactly one blank line separates the seeded template from the user body, and the seeded block does not retain its original trailing newlines. When `Body` is empty, the seeded block appears without a trailing separator. When `Template == Blank`, only the user body appears after frontmatter.
- **Parameterize:** `[Theory]` over `NoteTemplate` values {Blank, Daily, Meeting, Book, Idea} × body ∈ {"", "user text"}. For each row, precompute the seeded prefix via `NoteTemplates.SeedBody` and assert the captured `cli.CreatedBody` ends with the expected composition substring after the frontmatter fence.
- **Assertions:** Structural — split `CreatedBody` on `"---\n\n"` to isolate the body region, then assert `Assert.Equal(expectedComposedBody, bodyRegion)`. Do NOT substring-match the full body.
- **Mutations killed:**
  - Swap order to `user + "\n\n" + seeded.TrimEnd()` — caught by every non-Blank row.
  - Drop `TrimEnd()` — caught by any template whose seed ends with `\n\n` (all non-Blank seeds do): produces an extra blank line.
  - Change `"\n\n"` to `"\n"` — caught directly by the equality assertion.
  - Swap `seeded.Length == 0` / `user.Length == 0` guards — caught by the Blank+non-empty-body and non-Blank+empty-body rows respectively.

### P0-4 · `NoteTemplates.SeedBody` — per-template content and default branch
- **Contract:** Each `NoteTemplate` enum arm returns a distinct, non-empty seed containing an arm-specific section header; `Blank` returns `string.Empty`; the `default` (unknown enum value cast from `int`) returns `string.Empty`. `Daily` and `Meeting` interpolate the provided `DateTimeOffset` in `yyyy-MM-dd` / `yyyy-MM-dd HH:mm` form respectively.
- **Parameterize:** `[Theory]` rows: (Blank, "") — assert empty; (Daily, contains "# 2026-04-18" and "## Tasks"); (Meeting, contains "Attendees" and "2026-04-18 09:30"); (Book, contains "Rating" and "Highlights"); (Idea, contains "Problem" and "Sketch"). Additional `[Fact]`: `(NoteTemplate)999` → `string.Empty` (default branch).
- **Assertions:** `Assert.Contains` on the unique section marker (each marker appears in exactly one arm — verified by a guard `[Fact]` similar to the existing `DiscriminatorMarkers_AreActuallyUnique` pattern in `CardTemplatesTests`).
- **Mutations killed:**
  - Swapping two switch arms (e.g., Book ↔ Idea returning each other's body) — caught by unique-marker assertions.
  - Collapsing the `default` arm to `throw` — caught by the `(NoteTemplate)999` row.
  - Dropping the date interpolation in Daily/Meeting — caught by the date-substring assertion.

### P0-5 · `NoteCreationService` template-tag / user-tag dedup is case-insensitive
- **Contract:** When `TagsCsv` already contains a tag whose ordinal-case-insensitive form equals a template-supplied tag, the resulting frontmatter emits that tag exactly once, preserving the user's original casing.
- **Parameterize:** `[Theory]` over (TagsCsv, Template, expectedTagToken). Rows: ("Meeting", Meeting, "Meeting"); ("meeting", Meeting, "meeting"); ("MEETING,work", Meeting, "MEETING"); ("", Meeting, "meeting"); ("book,Book", Book, "book").
- **Assertions:** Parse `cli.CreatedBody`, locate the `tags: [...]` line, split on `, `, assert each row's expected tag appears exactly once and its template-default lowercase variant does **not** appear as a second entry.
- **Mutations killed:**
  - Changing `existing.Equals(t, StringComparison.OrdinalIgnoreCase)` to `Ordinal` — caught by the `"Meeting"` row (would emit both `Meeting` and `meeting`).
  - Removing the dedup predicate entirely — caught by the `"meeting"` row.
  - Reversing merge order (template tag first, user tag appended) — caught by the casing-preservation assertion on the `"MEETING,work"` row.

### P1-6 · `FolderPathValidator` boundary rows
- **Contract:** `"/"` alone validates to `Ok("")`; any empty segment between slashes (`"a//b"`, `"/a//b/"`) returns `Fail("Empty folder segment")`; any input whose second character is `':'` (covers `"X:"`, `"X:foo"`, `"ab"` — no, only len≥2 AND `[1]==':'`) returns `Fail("Absolute paths are not allowed")`; a leading-space segment `" foo/bar"` is rejected.
- **Parameterize:** Extend the existing `Validate_Normalizes` / `Validate_Rejects` theories with: `("/", "")` Ok; `("a//b", …)` fail; `("X:", …)` fail (2-char boundary); `(" foo", …)` fail; `("foo bar", "foo bar")` Ok (internal space allowed).
- **Assertions:** `IsValid` boolean + the specific error-message substring (`"Empty folder segment"`, `"Absolute paths are not allowed"`) — these are structural constants in the code.
- **Mutations killed:**
  - Changing `normalized.Length >= 2` to `> 2` — caught by the `"X:"` row.
  - Collapsing the empty-segment check to `IsNullOrEmpty` only — caught by `"a//b"` (would split into `["a","","b"]` where middle is empty but `IsNullOrWhiteSpace` catches it; `IsNullOrEmpty` alone still catches it — **this mutation is actually benign; drop this bullet**) — real kill: swapping `IsNullOrWhiteSpace(seg)` → `string.IsNullOrEmpty(seg)` is caught by a `" "` (single-space) segment case; add row `"a/ /b"` to force this.
  - Swapping the leading-slash `Trim('/')` for `TrimStart('/')` — caught by a row where trailing slash must be stripped before segment-splitting (`"/a/"` → "a" already exists; add `"a/"` explicit row if not present).

### P1-7 · `ObsidianWidgetProvider.ParseBool`
- **Contract:** Returns `fallback` when input is `null`; returns `true` only when input is ordinal-case-insensitive `"true"`; returns `false` for any other non-null string including `""`, `"True "` (trailing space), `"1"`, `"yes"`, `"TRUE"` (note: `"TRUE"` must return `true` per ignoreCase).
- **Parameterize:** `[Theory]` rows: `(null, true, true)` → true; `(null, false, false)` → false; `("true", false, true)`; `("TRUE", false, true)`; `("True", false, true)`; `("false", true, false)`; `("", true, false)`; `("1", true, false)`; `("yes", true, false)`; `(" true", true, false)` (leading space, strict match).
- **Assertions:** Boolean equality.
- **Mutations killed:**
  - `s is null ? fallback : …` → `s is null ? !fallback : …` — caught by both null rows.
  - `StringComparison.OrdinalIgnoreCase` → `Ordinal` — caught by `"TRUE"` row.
  - `Equals(s, "true")` → `Contains(s, "true")` — caught by `" true"` leading-space row.
  - Swapping `true` / `false` branches — caught by every row.

### P2-8 · `FrontmatterBuilder.YamlQuote` trigger coverage
- **Contract:** A tag is YAML-quoted iff it is empty or contains any of the characters `: # , [ ] { } & * ! | > ' " % @ \``; quoting wraps in `"..."` with `\` → `\\` and `"` → `\"` escaping.
- **Parameterize:** `[Theory]` one row per trigger character plus one empty-string row and one negative row (`"plain"`). For each trigger row, assert the emitted `tags: [...]` line contains a quoted token; for `plain` assert unquoted.
- **Assertions:** Extract the `tags: [...]` substring via regex `tags: \[(.+)\]`, split on `, `, index 0. Assert the token equals the expected `"..."`-wrapped form (or bare form for `plain`).
- **Mutations killed:**
  - Removing any character from `YamlQuoteTriggers` — caught by that character's row.
  - Dropping the `v.Length == 0` short-circuit — caught by the empty-tag row.
  - Swapping the `Replace("\\",…)` / `Replace("\"",…)` order — caught by a row containing both backslash and quote (add one such row).

### P2-9 · `JsonStateStore` atomic replace on Persist
- **Contract:** When `Save` is invoked, the live file at `_path` is never left in a partially written / truncated state; a pre-existing file remains readable until the move completes. A fake scenario: write a valid state, inspect `_path` + `.tmp`, re-read — the live file must deserialize cleanly and no `.tmp` sibling remains.
- **Parameterize:** `[Fact]` (singleton scenario). Assert: after `Save`, `File.Exists(_path + ".tmp")` is `false` and the live file deserializes to the saved state.
- **Assertions:** File existence + structural equality of the reloaded `WidgetState`.
- **Mutations killed:**
  - Replacing `File.Move(tmp, _path, overwrite: true)` with `File.Copy(tmp, _path, overwrite: true)` — caught by the `.tmp` leftover assertion.
  - Writing directly to `_path` instead of `tmp` — the contract assertion still passes, but leaves the file vulnerable; this mutation is weakly killed. Consider dropping from the "must-fail" list and flagging as property-based only.
- **Note:** This is the weakest of the proposals for DDM rigour; include only if the P0/P1 set is already in place.

### P2-10 · `FileLog.Roll` at MaxBytes boundary
- **Contract:** When the log file size exceeds `MaxBytes` (1,000,000), the next write rotates the file to `<path>.1` (replacing any prior `.1`) and resumes with a fresh primary file. Writes on a small file do not rotate. All write methods never throw even when the file is locked by another process.
- **Parameterize:** Three `[Fact]`s (or one `[Theory]` with (initialSize, expectRotation) rows): size=0 → no rotation; size=MaxBytes exactly → no rotation (strict `>`); size=MaxBytes+1 → rotation; pre-existing `.1` file → overwritten.
- **Assertions:** `File.Exists(path + ".1")` + byte-length of primary file after the triggering write.
- **Mutations killed:**
  - `Length > MaxBytes` → `>=` — caught by the boundary-exact row.
  - Removing the `if (File.Exists(old)) File.Delete(old)` guard — caught by the "pre-existing .1" row (would throw on `Move` without the delete, and without the surrounding `try/catch` would surface; currently swallowed — test would rely on post-state rather than exception).
  - Swapping `File.Move(_path, old)` to `File.Copy(_path, old)` — caught by asserting `new FileInfo(_path).Length == line.Length` after rotation (not ≈ MaxBytes).

---

## 4. Deliberately skipped

| Area | Reason |
|---|---|
| `ObsidianCli` process invocation | Requires a real `obsidian` binary or heavy `IProcessRunner` extraction refactor — that's `refactorer` territory, not test-author. |
| Tray `QuickNoteForm` behavior | WinForms; would need a controller extraction first. Flag for future. |
| COM `ClassFactory` / `Ole32` | Unit-untestable P/Invoke glue. |
| `GlobalHotkey` | P/Invoke; untestable without a message-loop harness. |
| `WidgetState` POCO direct tests | Transitively asserted via `JsonStateStore` round-trip theory. |

---

## 5. Top-3 recommendations (returned to caller)

1. **`ObsidianWidgetProvider.ParseInputs`** (P0-1) — user-controlled JSON entry point, zero coverage today, four distinct mutations killable with one `[Theory]`. Requires `InternalsVisibleTo` on the Widget assembly.
2. **`ObsidianWidgetProvider.RememberRecent`** (P0-2) — pure static LRU helper; misbehavior is user-visible on every successful note create; four-mutation kill set via a single parameterized theory.
3. **`NoteCreationService` template-body composition** (P0-3) — the `seeded.TrimEnd() + "\n\n" + user` join is the only place templated notes become malformed without any other test catching it; parameterize across all five `NoteTemplate` values × {empty body, non-empty body} to pin the contract.
