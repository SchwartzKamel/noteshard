# Test-Author Coverage Audit — v3 (delta sweep)

**Scope:** `src/ObsidianQuickNoteWidget.Core`, `src/ObsidianQuickNoteWidget`, `src/ObsidianQuickNoteTray`
**Test project:** `tests/ObsidianQuickNoteWidget.Core.Tests` (xUnit). Widget assembly **still has no test project** and no `InternalsVisibleTo` directive.
**Mandate:** Read-only. Inventory new surfaces since v2, re-score prior gaps, surface the highest-leverage net-new gap.

---

## 1. New SUT surface since v2: `folderNew` precedence in `ObsidianWidgetProvider.CreateNoteAsync`

```csharp
// src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:250–254
var title     = inputs.GetValueOrDefault("title") ?? string.Empty;
var folderNew = inputs.GetValueOrDefault("folderNew")?.Trim();
var folder    = !string.IsNullOrEmpty(folderNew)
    ? folderNew
    : inputs.GetValueOrDefault("folder") ?? state.LastFolder;
```

This three-tier precedence (`folderNew` typed → `folder` picker → `state.LastFolder`) is the single user-visible escape hatch for typing a brand-new folder in the medium card. It is also the only input on the path that is `.Trim()`-ed before a presence check, which means behavior differs across `null`, `""`, `"   "`, `"  Foo  "`, and `"Foo"` in ways the call site cannot see.

### Testability status

| Factor | State |
|---|---|
| Symbol location | `ObsidianQuickNoteWidget` (widget COM assembly) — private member of provider class |
| Test project exists for widget assembly? | **No** |
| `InternalsVisibleTo` on widget assembly? | **No** (only Core has it) |
| Referenced anywhere in `tests/`? | Only the string `"folderNew"` appears in `CardTemplatesTests.cs` (placeholder-marker assertion on the card JSON) — the *precedence logic* is never exercised. |
| Reachable from Core? | No. `CreateNoteAsync` lives on the provider; it calls `INoteCreationService.CreateAsync` but does its own `inputs` merging first. A `NoteCreationServiceTests` fake can observe what `folder` value was passed in, but only if a test can *drive the provider* — which currently nothing can. |

**Verdict: UNCOVERED, and structurally unreachable by the existing test project.** A `folderNew → folder → state.LastFolder` swap, dropping `.Trim()`, or swapping `!IsNullOrEmpty` for `!IsNullOrWhiteSpace` would all survive the entire v2 suite of 199 tests. This is the **highest-value net-new coverage gap** introduced since v2 — it silently governs note destination for every new-folder user flow.

---

## 2. Coverage delta vs v2 Top-3

| v2 rank | Gap | v3 status | Evidence |
|---|---|---|---|
| Top-1 | `NoteCreationService.BuildBody` composition (`seeded.TrimEnd() + "\n\n" + user`) | **UNCHANGED — PARTIAL.** No new theory over `NoteTemplate × {empty,"user"}` added. `Create_TemplateSeedsBody` still the sole seeded-body fact. Separator `\n\n → \n` mutation still survives. |
| Top-2 | `ObsidianWidgetProvider.{ParseInputs, ParseBool, RememberRecent}` trio | **UNCHANGED — UNCOVERED.** Widget assembly still has no test project, no `InternalsVisibleTo`. Prior blocker persists. And now `CreateNoteAsync`'s input-merging joins the trio as a **fourth** untestable-in-practice surface. |
| Top-3 | `FileLog.Roll` 1 MB boundary + unwritable-file swallow | **UNCHANGED — UNCOVERED.** `FileLogTests.cs` still only exercises `SanitizeForLogLine`. No test pre-seeds the log past `MaxBytes`; `.1` overwrite path unverified; strict `>` vs `>=` boundary survives mutation. |

No v2-ranked gap was closed this pass. Suite size unchanged at ~199 (no new tests added for either the v2 Top-3 or the new `folderNew` surface).

---

## 3. Top-3 (v3)

### **Top-1 · `ObsidianWidgetProvider.CreateNoteAsync` — `folderNew` precedence, trim, and empty-string edge** *(NEW; net-new surface)*

**Contract to pin (parameterized theory over inputs → observed `NoteRequest.Folder` passed to a fake `INoteCreationService`):**

| Row | `folderNew` input | `folder` input | `state.LastFolder` | Expected `req.Folder` | Kills mutation |
|---|---|---|---|---|---|
| 1 | `"Projects/New"` | `"Picker/Old"` | `"Last"` | `"Projects/New"` | Drop `!IsNullOrEmpty(folderNew)` guard → would fall through to `folder`. |
| 2 | `"  Projects/New  "` | `null` | `"Last"` | `"Projects/New"` | Drop `.Trim()` → would persist whitespace into `state.LastFolder`. |
| 3 | `"   "` (whitespace only) | `"Picker"` | `"Last"` | `"Picker"` | Swap `!IsNullOrEmpty` → `!IsNullOrWhiteSpace` contract is already implicit via `.Trim()` first — this row pins the exact order (trim-then-empty-check, not whitespace-check). |
| 4 | `""` | `"Picker"` | `"Last"` | `"Picker"` | Drop the guard entirely → would return `""` and overwrite `state.LastFolder` with empty. |
| 5 | `null` | `"Picker"` | `"Last"` | `"Picker"` | Swap operand order of the null-coalesce (`folder ?? state.LastFolder` → `state.LastFolder ?? folder`) would still pass row 5 alone; pair with row 6 to kill it. |
| 6 | `null` | `null` | `"Last"` | `"Last"` | Pair with row 5: together they force `folder ?? state.LastFolder` and nothing else. |
| 7 | `null` | `""` | `"Last"` | `""` | Pins that the `folder` picker's empty string is *not* treated as absent — swapping `inputs.GetValueOrDefault("folder") ?? state.LastFolder` to `string.IsNullOrEmpty(...) ? state.LastFolder : ...` would fail this row. (If the intended contract is the opposite, this test flags the ambiguity — a `bug-hunter` question.) |
| 8 | `"Projects/New"` | — | `"Last"` | post-create: `state.LastFolder == "Projects/New"` | Pins that the *persisted* state mirrors the resolved folder (drops any mutation that writes `folder` before precedence resolution). |

**Blocker & unblock cost:** Same as v2 Top-2 — one `[assembly: InternalsVisibleTo("ObsidianQuickNoteWidget.Tests")]` on the widget assembly plus a new xUnit project referencing it. The provider already has an `internal` constructor `(ILog, IStateStore, IObsidianCli?, INoteCreationService?, AsyncKeyedLock<string>?)` with full DI, so `CreateNoteAsync` is drivable once visible.

**Why Top-1 now:** This is the only surface in the repo where user-typed text directly becomes a filesystem path *and* goes through silent normalization (`Trim`) *and* has no downstream validator (`FolderPathValidator` runs later but accepts `"  "` → empty after trim inside itself, masking the bug). The combined mutation-yield per test row is the highest in the codebase.

### **Top-2 · `NoteCreationService.BuildBody` composition contract** *(was v2 Top-1, unchanged priority but demoted by Top-1)*

- **Why still here:** Zero progress since v2. Fixtures already exist (`FakeCli.CreatedBody`, `FixedTimeProvider`). A `[Theory]` over `NoteTemplate × {null, "", "user text"}` is a one-file addition.
- **Mutations to kill:** swap-order (`user + sep + seeded`), `TrimEnd()` removal, `\n\n → \n`, `seeded.Length == 0` ↔ `user.Length == 0` guard swap.
- **Side benefit:** Collapses v1 gaps #3 and #4 (template `SeedBody` arm coverage) into one theory via seeded-prefix capture.

### **Top-3 · `folderNew`-adjacent: provider-side state persistence after `CreateNoteAsync`** *(NEW; companion to Top-1)*

The `state.LastFolder = folder ?? string.Empty;` write at line 282 means **whatever Top-1 resolves is also what gets persisted**. A single extra case per Top-1 row asserting `store.Get(id).LastFolder` after the await pins the full round-trip. Same blocker (widget test project) as Top-1, same fix, near-zero marginal cost once Top-1 is wired.

Specific mutations this kills that Top-1 alone does not:
- Moving the `state.LastFolder =` line *above* the `folder` resolution (would persist the raw picker value, not the typed `folderNew`).
- Replacing `folder ?? string.Empty` with `folder ?? state.LastFolder` (would make the persisted value sticky across null-resolve, which is the opposite of the observed "forget" semantic when both inputs are absent — but row 6 of Top-1 already pins `folder="Last"`, so this row needs a distinct case: both `folderNew` and `folder` null **and** `state.LastFolder=""` → assert `state.LastFolder` remains `""`, not nulled or unchanged).

---

## 4. Other deltas worth flagging (below Top-3 but new since v2)

| Surface | Status |
|---|---|
| `ObsidianWidgetProvider.CreateNoteAsync` — `!_active.ContainsKey(session.Id) return;` early-exit (line 247) | **UNCOVERED.** The "widget was deactivated mid-action" branch. Distinct from Top-1 but same blocker. |
| `ObsidianWidgetProvider.CreateNoteAsync` — `PendingBodyPaste` merge (lines 262–268) — empty-body → paste, non-empty → `body + "\n\n" + paste` | **UNCOVERED.** Same separator-contract as `BuildBody` (Top-2) but on a different code path. Mutation `\n\n → \n` survives. |
| `ObsidianWidgetProvider.CreateNoteAsync` — `Enum.TryParse<NoteTemplate>(template, ignoreCase: true, ...)` fallback to `Blank` (line 275) | **UNCOVERED.** Dropping `ignoreCase: true` or swapping the fallback from `Blank` to `Daily` would survive. |
| `state.Template = template ?? "Blank"` post-mutation (line 287) | **UNCOVERED.** The persisted-template fallback when `template` is null. Distinct from the `Enum.TryParse` fallback above — one governs what gets *used*, the other governs what gets *remembered*. A mutation swapping `?? "Blank"` for `?? state.Template` would hold LastTemplate sticky instead of resetting, and would survive. |

All four share the **same single blocker** as Top-1/Top-3: widget-assembly `InternalsVisibleTo` + a test project. One PR unlocks seven high-value theories.

---

## 5. Numbers at a glance

- v2 Top-3 closed this pass: **0 / 3**
- v2 Top-3 still open: **3 / 3** (all UNCOVERED or PARTIAL, unchanged)
- Net-new uncovered surfaces surfaced this sweep: **5** (`folderNew` precedence, `folderNew`-trim, `!_active` early-exit, `PendingBodyPaste` merge, `Enum.TryParse`/`state.Template` fallback)
- All five sit behind **one** structural blocker: no test project for `ObsidianQuickNoteWidget`.
- Suite size: **199** (unchanged).

**Single highest-leverage action for v4:** land the widget test project + `InternalsVisibleTo`. That one piece of scaffolding unblocks v2 Top-2 (three pure helpers), v3 Top-1 (`folderNew` precedence, 8 theory rows), v3 Top-3 (state persistence), and the four §4 surfaces — **~20 mutation-killing cases for one project-file change.**
