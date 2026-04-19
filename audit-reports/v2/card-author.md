# card-author audit v2 — Adaptive Card templates

**Scope:** `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/*.json` + `CardTemplates.cs` + `CardDataBuilder.cs`
**Mode:** read-only
**Verdict:** 🟢 **Mostly clean.** v1 CRITICAL + HIGH findings all fixed. Two MEDIUM items from v1 linger (large-card action budget, compact-folder UX wording partially fixed), plus one new LOW.

---

## v1 → v2 verification table

| v1 # | v1 Severity | Finding | v2 Status | Evidence |
|------|-------------|---------|-----------|----------|
| 1 | CRITICAL | `"version": "1.6"` on all 5 templates | ✅ **FIXED** | `small:4`, `medium:4`, `large:4`, `RecentNotes:4`, `CliMissing:4` all declare `"version": "1.5"`. |
| 2 | HIGH | Missing `widgetId` on Action.Submit/Execute + selectAction | ✅ **FIXED** | Every `Action.Execute` across all 5 templates carries `"data": { "widgetId": "${widgetId}", "verb": "<verb>" }`. The `selectAction` on `RecentNotes.json:21-25` resolves `widgetId` via `${$root.widgetId}` (needed since `$data` shifts scope into `recentNotes[*]`). Large template no longer has recents rows, so the second selectAction from v1 is gone. |
| 3 | HIGH | `CardDataBuilder` never emits `widgetId` | ✅ **FIXED** | `CardDataBuilder.cs:21` sets `["widgetId"] = s.WidgetId ?? string.Empty`; `BuildCliMissingData` takes `widgetId` parameter and emits it at `:51`. `WidgetState.WidgetId` is the canonical source (`WidgetState.cs:6`). |
| 4 | MEDIUM | Compact ChoiceSet "type new or pick" lie | 🟡 **PARTIALLY FIXED** | Both medium (`:18`) and large (`:20`) now use `"style": "expanded"` — good (meets v2 task requirement). But medium placeholder still reads `"Folder (type new or pick)"` even though an expanded ChoiceSet (radio list) cannot accept free-text either. Wording still misleads users. Large placeholder is a gentler `"Type or pick"` — same issue, softer. |
| 5 | MEDIUM | Large has 3 actions (budget = 2) | ❌ **NOT FIXED** | `QuickNote.large.json:120-140` still carries three `Action.Execute`s: `createNote`, `pasteClipboard`, `toggleAdvanced`. Archetype rule: *"Large ≈ … + 2 actions."* |
| 6 | MEDIUM | Large has inline recents list inside full form | ✅ **FIXED** | Large template now ends at a status `TextBlock` (`:112-118`) — no recents container or heading. The dedicated `RecentNotes.json` widget kind owns that surface. |
| 7 | LOW | `(vault root)` sentinel prepended to `folderChoices` | ⚠️ **UNCHANGED** | `CardDataBuilder.cs:60-63` still prepends the vault-root sentinel ahead of pinned/recent/cached. Still benign; still undocumented. |
| 8 | LOW | Medium body has no `label` | ⚠️ **UNCHANGED** | `QuickNote.medium.json:30-35` — `Input.Text id=body` has no `label`. Accessibility nit only. |

---

## Fresh v2 checks

### Unique element IDs per template
| Template | IDs | Unique? |
|----------|-----|---------|
| `QuickNote.small.json` | `title` | ✅ |
| `QuickNote.medium.json` | `title`, `folder`, `body` | ✅ |
| `QuickNote.large.json` | `title`, `folder`, `body`, `tagsCsv`, `template`, `autoDatePrefix`, `openAfterCreate`, `appendToDaily` | ✅ |
| `RecentNotes.json` | (no inputs) | ✅ |
| `CliMissing.json` | (no inputs) | ✅ |

### Theme-neutrality
All color references use AC roles (`Attention`, `Accent`, `Good`, `Default`) or data-bound `${statusColor}`. No hex literals anywhere, no dark-mode-specific assumptions. ✅

### `$when` usage
Zero occurrences across all 5 templates. All conditionals use `isVisible` bound to boolean data fields (`hasStatus`, `hasDetail`, `hasNoRecents`, `showAdvanced`). ✅

### `${...}` bindings resolve against `CardDataBuilder` output
- **small** → `widgetId`, `inputs.title`, `statusMessage/Color`, `hasStatus` ✅
- **medium** → adds `inputs.folder/body`, `folderChoices[].title/value` ✅
- **large** → adds `inputs.tagsCsv/template/autoDatePrefix/openAfterCreate/appendToDaily`, `showAdvanced`, `advancedLabel` ✅
- **RecentNotes** → `widgetId`, `recentNotes[].title/path`, `hasNoRecents`, status triple ✅
- **CliMissing** → `widgetId`, `detail`, `hasDetail` ✅

No unresolved tokens. Large template's unused `hasRecents` field is emitted but never bound (harmless leftover after recents were removed from Large — see new issue below).

### 1.6-only features
None detected. `Input.ChoiceSet style="expanded"` is 1.0+; `Input.Toggle valueOn/valueOff` is 1.0+; `Action.Execute` is 1.4+; no `Input.ChoiceSet.Filtered`, no `Refresh`/`authentication` blocks, no 1.6 `InputValidation`, no `RichTextBlock` 1.6 additions. ✅

---

## New issues (v2)

| # | Severity | File | Finding | Fix |
|---|----------|------|---------|-----|
| N1 | MEDIUM | `QuickNote.medium.json:17` | Folder ChoiceSet placeholder `"Folder (type new or pick)"` — expanded style is a radio list; typing is still impossible. Same trap as v1 issue #4, just in expanded clothing. | Change placeholder to `"Pick a folder"` (or drop placeholder since expanded ChoiceSets don't render one anyway). If a "new folder" path is desired, pair with an `Input.Text id=newFolder`. |
| N2 | LOW | `CardDataBuilder.cs:41` + `QuickNote.large.json` | `hasRecents` is still emitted on the data object but no template binds it anymore (recents were removed from Large in v1 fix #6; `RecentNotes.json` uses `hasNoRecents` instead). Dead field. | Either drop `["hasRecents"]` from `BuildQuickNoteData`, or retain and document as "reserved for future Large inline recents toggle." Harmless either way. |
| N3 | LOW | `QuickNote.medium.json:17` vs `QuickNote.large.json:19` | Placeholder strings on the folder ChoiceSet diverge (`"Folder (type new or pick)"` vs `"Type or pick"`). Minor consistency nit for the same control. | Align copy across medium/large. |

v1 issues still open: **#5 (Large 3-action budget)** and **#4 wording** are the substantive lingerers; **#7, #8** remain cosmetic.

---

## Template-level confirmations (task checklist)

- ✅ All 5 templates declare `"version": "1.5"`.
- ✅ Every `Action.Execute` (6 across small/medium/large + 2 RecentNotes + 1 CliMissing = 9) carries `data.widgetId` via `${widgetId}`.
- ✅ Single `selectAction` (RecentNotes.json recent-row) carries `data.widgetId` via `${$root.widgetId}` — correct scope root-escape under `$data`.
- ✅ Folder ChoiceSet is `"style": "expanded"` on both medium and large; no `compact` remains.
- ✅ Large template has no inline recents container — budget-wise only the 3-action overrun remains.
- ✅ `CardDataBuilder.BuildQuickNoteData` emits `["widgetId"] = s.WidgetId ?? ""` (line 21) matching `WidgetState.WidgetId` (WidgetState.cs:6); `BuildCliMissingData` takes explicit `widgetId` param.

---

## Top 3 (priority order)

1. **Trim Large card `actions[]` to 2** — move `toggleAdvanced` into an inline `ActionSet` inside the body (or into a header `Action.ToggleVisibility`, which is 1.2+, schema-safe). This is the only remaining archetype violation.
2. **Fix the folder ChoiceSet placeholder wording on medium (N1)** — `"Folder (type new or pick)"` still promises free-text entry that expanded radio-style ChoiceSets don't support. Align large's softer `"Type or pick"` at the same time (N3).
3. **Decide on the `hasRecents` data field (N2) + document the `(vault root)` sentinel (v1 #7)** — two small janitorial items in `CardDataBuilder` to match template reality and archetype ordering semantics.

---

*Read-only audit. No source files modified.*
