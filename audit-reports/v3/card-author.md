# card-author audit v3 — Adaptive Card templates

**Scope:** `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/*.json` + `CardTemplates.cs` + `CardDataBuilder.cs`
**Mode:** read-only
**Verdict:** 🟠 **One new HIGH (data-shape regression on the new `folderNew` input).** Schema + widgetId discipline still clean. Post-revert folder picker is back to `compact` on medium+large with a sibling `Input.Text id=folderNew` handling the "new folder" path.

---

## Task-driven verification

### (a) Schema version = 1.5 on every template
All five templates declare `"version": "1.5"`:
- `QuickNote.small.json:4`, `QuickNote.medium.json:4`, `QuickNote.large.json:4`, `RecentNotes.json:4`, `CliMissing.json:4`. ✅

No 1.6-only features found (no `Input.ChoiceSet.Filtered`, no `RichTextBlock` 1.6 adds, no `Refresh`/`authentication`, no 1.6 `InputValidation`). ✅

### (b) `widgetId` bound on every action
| Template | Action / selectAction | `data.widgetId` |
|----------|-----------------------|-----------------|
| small    | `createNote` (:30) | `${widgetId}` ✅ |
| medium   | `createNote` (:58), `pasteClipboard` (:64) | both `${widgetId}` ✅ |
| large    | `createNote` (:133), `pasteClipboard` (:139), `toggleAdvanced` (:145) | all `${widgetId}` ✅ |
| RecentNotes | selectAction `openRecent` (:24), `createNote` (:52), `openVault` (:58) | selectAction uses `${$root.widgetId}` (scope-correct under `$data`), others use `${widgetId}` ✅ |
| CliMissing | `recheckCli` (:31) | `${widgetId}` ✅ |

No action ships without `widgetId`. ✅

### (c) `folderNew` id uniqueness per template
| Template | Input IDs | Collision? |
|----------|-----------|------------|
| medium | `title`, `folder`, `folderNew`, `body` | ✅ unique |
| large  | `title`, `folder`, `folderNew`, `body`, `tagsCsv`, `template`, `autoDatePrefix`, `openAfterCreate`, `appendToDaily` | ✅ unique |
| small  | `title` | ✅ |
| RecentNotes / CliMissing | (no inputs) | ✅ |

`folderNew` does not collide with `folder` or any other id, in either template. ✅

### (d) `${$root.inputs.folderNew}` — does `CardDataBuilder` echo it? **NO.** 🔴

`QuickNote.medium.json:33` and `QuickNote.large.json:36` both bind:
```
"value": "${$root.inputs.folderNew}"
```
`CardDataBuilder.BuildQuickNoteData` (lines 22-32) emits an `inputs` object containing:
```
title, folder, body, tagsCsv, template, autoDatePrefix, openAfterCreate, appendToDaily
```
— **`folderNew` is not in the list.** The binding therefore resolves against an undefined property. Consequences on any re-render (status push, `toggleAdvanced`, post-create reset, CLI-missing → ready swap):

1. The Widget Host renderer replaces the `value` with either an empty string or, worse, the literal `${$root.inputs.folderNew}` token — depending on how strict the host's templating engine is. Either way, **whatever the user just typed is wiped** when the provider re-issues the card.
2. The submit path still works the first time (before any re-render) because `ObsidianWidgetProvider.cs:251` reads `inputs["folderNew"]` from the client submit payload — that path is client→server and doesn't depend on the echo. But as soon as the widget host re-renders (e.g. "Creating…" → "Created ✓" status swap, or the advanced toggle), the user's in-flight new-folder text disappears mid-session.
3. Worst case the user toggles advanced on Large after typing a new folder → new folder value evaporates → they hit Create → note is saved to the old `folder` picker value instead. Silent data misroute.

This is the same class of bug the spec calls out: *"Never bind `${...}` to a field `CardDataBuilder` doesn't produce — silent render failure."*

**Fix:** Add `["folderNew"] = string.Empty` to the `inputs` JsonObject in `BuildQuickNoteData` (CardDataBuilder.cs:22-32). The empty string matches the "start-empty each render" semantics of `title` and `body`. If the intent is to *persist* across re-renders within the same card lifetime, the provider must also capture the latest submitted `folderNew` into `WidgetState` and echo it back here — but that's a separate design call; the minimum fix to avoid the literal-token render is the empty echo.

---

## Other verification

### Compact ChoiceSet revert is consistent
Medium (`:20`) and Large (`:20`) both declare `"style": "compact"`. No leftover `expanded` anywhere. ✅ The old v2 N1/N3 placeholder-wording complaints are moot now — `"Pick folder"` on both is honest for a dropdown.

### folderNew UX pair
- Medium `:29-34` — `Input.Text id=folderNew`, no label, placeholder `"…or type new folder (optional)"`. Sits directly below the compact folder picker. Clean.
- Large `:31-37` — `Input.Text id=folderNew`, `label: "New folder"`, placeholder `"Optional — overrides picker above"`. Clean and explicit.
- `ObsidianWidgetProvider.cs:251-253` confirms resolution precedence: if `folderNew` is non-empty it wins over `folder`. Matches the Large placeholder wording. ✅

### Binding resolution — full sweep
Every `${...}` in every template maps to a field emitted by `BuildQuickNoteData` / `BuildCliMissingData` **except** the new `inputs.folderNew` binding. That's the only broken edge.

### Schema / guardrail residue (unchanged from v2)
- `hasRecents` still emitted on data object (CardDataBuilder.cs:41) and still unbound by any template. Dead field — v2 N2 still open.
- `(vault root)` sentinel still prepended to `folderChoices` ahead of pinned (CardDataBuilder.cs:60-63) — v1 #7 still open, still benign.
- Large still ships 3 `actions[]` (`createNote`, `pasteClipboard`, `toggleAdvanced`) — v1 #5 still open, exceeds archetype "Large ≈ … + 2 actions" budget.

---

## New v3 issues

| # | Severity | File | Finding | Fix |
|---|----------|------|---------|-----|
| V3-1 | **HIGH** | `CardDataBuilder.cs:22-32` + `QuickNote.medium.json:33` + `QuickNote.large.json:36` | Templates bind `${$root.inputs.folderNew}` but `BuildQuickNoteData` does not emit `inputs.folderNew`. On any re-render the user's typed new-folder text either renders as the literal binding string or is wiped — silent data loss. | Add `["folderNew"] = string.Empty` (or the persisted value from `WidgetState` if you want sticky behavior) to the `inputs` JsonObject in `BuildQuickNoteData`. |
| V3-2 | LOW | `QuickNote.medium.json:29-34` | `Input.Text id=folderNew` has no `label` (only `placeholder`). Medium is short on visual real estate but screen readers will announce it as a bare textbox adjacent to the folder dropdown — confusing. Large has a label. | Add `"label": "New folder"` for parity with Large, or at least `"isVisible": "${$root.showAdvanced}"`-equivalent progressive disclosure (not applicable on Medium since there's no advanced mode — just add the label). |
| V3-3 | LOW | Medium + Large | `folderNew` is never wiped after a successful create. If the provider re-renders the card after `createNote` and V3-1 is fixed with an echo of `WidgetState.LastFolderNew` (or similar), stale new-folder text can linger into the next note's submit. If V3-1 is fixed with `string.Empty`, this is moot. Flag so the fix chooses consciously. | When fixing V3-1, explicitly decide: empty each render (safe, loses typing on re-render) **or** sticky via state (requires clearing on create-success). Document the choice. |

---

## Top 3 (priority order)

1. **V3-1 — Emit `inputs.folderNew` from `CardDataBuilder.BuildQuickNoteData`.** The reverted-to-compact ChoiceSet design depends on this new input; without the echo, any re-render silently drops the user's input and can misroute the note to the old picker value. Minimum fix: one line (`["folderNew"] = string.Empty,` after `["folder"]` at line 25). Decide sticky-vs-empty per V3-3.
2. **Trim Large `actions[]` to 2** (carry-over v1 #5). Move `toggleAdvanced` into an inline `ActionSet` inside the body container so the action rail stays within the Large card archetype budget.
3. **Janitorial:** drop unused `hasRecents` from `BuildQuickNoteData` (v2 N2) and either document or relocate the `(vault root)` sentinel in `BuildFolderChoices` (v1 #7). Low-risk consistency cleanup.

---

*Read-only audit. No source files modified.*
