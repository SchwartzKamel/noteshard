# card-author audit — Adaptive Card templates

**Scope:** `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/*.json` + `CardTemplates.cs` + `CardDataBuilder.cs`
**Mode:** read-only
**Verdict:** 🟠 **Needs work — one CRITICAL spec violation across every template; several MEDIUM UX/routing gaps.**

The bindings, dedup/order of `folderChoices`, template registration, `isVisible` discipline, unique-ID rule, and theme-neutrality are all clean. The blockers are (1) every template declares an unsupported schema version and (2) no submit action carries `widgetId` in its `data` block — both required by the card-author spec.

---

## Issue matrix

| # | Severity | File / Card | Finding | Fix |
|---|----------|-------------|---------|-----|
| 1 | **CRITICAL** | `QuickNote.small.json`, `QuickNote.medium.json`, `QuickNote.large.json`, `RecentNotes.json`, `CliMissing.json` (all 5) | `"version": "1.6"`. Spec pins Widget Host max to `"1.5"`; 1.6 features will not render. | Change `"version"` to `"1.5"` in all five templates. No 1.6-only features are actually used, so the downgrade is mechanical. |
| 2 | **HIGH** | All templates with actions (small/medium/large/RecentNotes/CliMissing) | No `Action.Execute` (spec term: `Action.Submit`) carries a `data` payload with `widgetId`. Spec: *"Every Action.Submit must include widgetId in its data object."* The `selectAction` on recent-note rows in `QuickNote.large.json` (line 129‑133) and `RecentNotes.json` (line 21‑25) send `data: { "path": "${path}" }` — `widgetId` still missing. | Add `"data": { "widgetId": "${widgetId}", "verb": "<verb>" }` (or merge into existing `data`) on every `Action.Execute`. Requires #3 below. |
| 3 | **HIGH** | `CardDataBuilder.cs` | `BuildQuickNoteData` / `BuildCliMissingData` never emit a `widgetId` field, so `${widgetId}` can't resolve even if templates added it. Same for `BuildCliMissingData`. | Thread `widgetId` through the builder signatures (`BuildQuickNoteData(WidgetState s, bool showAdvanced, CardStatus? status, string widgetId)` — or just read `s.WidgetId`, which already exists on `WidgetState`) and set `root["widgetId"] = s.WidgetId`. |
| 4 | **MEDIUM** | `QuickNote.medium.json` (line 17), `QuickNote.large.json` (line 19) | `Input.ChoiceSet` with `"style": "compact"` + placeholder `"Folder (type new or pick)"` / `"Type or pick"`. Compact ChoiceSet is a dropdown — users **cannot type** a new folder. Placeholder is a lie; new-folder path is unreachable from the card. | Either switch to `Input.Text` with suggestion support (not available in 1.5 so effectively a plain `Input.Text` fallback), drop the "type new" wording, or add a separate `Input.Text id=newFolder` next to the ChoiceSet and let the verb handler prefer it when non-empty. |
| 5 | **MEDIUM** | `QuickNote.large.json` | 3 actions (`Create`, `Paste → body`, `${advancedLabel}`) exceeds spec budget of 2. Spec large ≈ "full form … + 2 actions." | Move `toggleAdvanced` to an inline `Action.ToggleVisibility` / header control, or fold it into an `ActionSet` inside the body, or demote `Paste → body` to an icon inside the body `ColumnSet`. Keep `actions[]` ≤ 2. |
| 6 | **MEDIUM** | `QuickNote.large.json` (lines 112‑136) | Recent-notes list and heading live inside the Large card in addition to the full form. Combined with 3 toggles + 5 inputs + status + 3 actions this risks clipping in the Widget Board shell (spec: *"Over-packing clips content"*). | Drop recents from Large (that's what the dedicated `RecentNotes.json` kind is for), or gate the recents container on `showAdvanced` being **false** so it doesn't compound with the advanced block. |
| 7 | **LOW** | `CardDataBuilder.cs` `BuildFolderChoices` (line 58‑61) | A synthetic `{ "(vault root)", "" }` entry is prepended ahead of the pinned/recent/cached sequence. Not a reorder per se (the pinned → recent → cached sequence is preserved and dedup is precedence-correct), but the spec ordering says *"pinned first"* — the vault-root sentinel is an undocumented prefix. | Either document the sentinel explicitly as "always-first vault-root option, then pinned → recent → cached" or move it to the end as a fallback. Currently benign but worth pinning down. |
| 8 | **LOW** | `QuickNote.medium.json` | `Input.Text id=body` has no `label`. Small/medium accessibility nit; Large uses labels throughout. | Add `"label": "Body"` (or drop labels on Large for consistency). |
| 9 | **INFO** | `QuickNote.small.json` | Small card = 1 input + 1 action + a (conditionally visible) status line. Within budget. ✓ |  |
| 10 | **INFO** | All templates | No `$when` usage anywhere — all conditionals use `isVisible`. ✓ |  |
| 11 | **INFO** | All templates | No `Image` elements, so the `altText` rule is vacuously satisfied. If icons/thumbnails are added later, each will need `altText`. |  |
| 12 | **INFO** | All templates | No hardcoded hex; colors use AC roles (`Attention`, `Accent`, `Good`, `Default`) via direct values or `${statusColor}` binding. ✓ |  |
| 13 | **INFO** | All templates | Input `id`s are unique within each card. ✓ |  |
| 14 | **INFO** | `CardTemplates.cs` + `ObsidianWidgetProvider.cs` (lines 305/310/315) | All five templates are registered as constants and dispatched: `CliMissing` on CLI-missing, `RecentNotes` on that widget kind, `LoadForSize` for small/medium/large otherwise. ✓ |  |

## Binding resolution check (every `${...}` → `CardDataBuilder` field)

Verified each template's tokens resolve against the data object produced by `BuildQuickNoteData` / `BuildCliMissingData`. Only missing-but-expected field is `widgetId` (issue #3).

- **QuickNote.small.json** — `inputs.title`, `statusMessage`, `statusColor`, `hasStatus` → all present ✅
- **QuickNote.medium.json** — adds `inputs.folder`, `inputs.body`, `folderChoices[].title/value` → all present ✅
- **QuickNote.large.json** — adds `inputs.tagsCsv/template/autoDatePrefix/openAfterCreate/appendToDaily`, `showAdvanced`, `advancedLabel`, `recentNotes[].title/path`, `hasRecents` → all present ✅
- **RecentNotes.json** — `recentNotes[].title/path`, `hasNoRecents`, `statusMessage/Color/hasStatus` → all present ✅
- **CliMissing.json** — `detail`, `hasDetail` → all present ✅

No silent-fail bindings.

## Top issues (priority order)

1. **Downgrade `"version"` from `1.6` → `1.5`** on all five templates. Blocking per spec (renderer).
2. **Emit `widgetId` from `CardDataBuilder` and bind it** into every `Action.Execute.data` block (and the two `selectAction.data` blocks in large / RecentNotes). Required by spec for provider routing, even though the current provider happens to route via `WidgetSession`.
3. **Fix the compact-ChoiceSet "type new folder" lie** in medium/large (issue #4) — it's a dead UX path today.
4. **Trim Large card to budget** — 3 actions and a recents list inside the full form combine to exceed the documented density (issues #5, #6).

---

*Read-only audit. No source files modified.*
