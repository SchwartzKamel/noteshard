# Adaptive Cards contract

> This page is for contributors editing a card template or a `CardDataBuilder` field — the two must move in lockstep or inputs silently blank on the next render.

Up: [`../README.md`](../README.md) (docs index)

## Schema pinning

Every template is `"version": "1.5"`. The Widget Host renderer **silently
drops 1.6 features** — the v1 card-author audit landed on 1.5 after watching
`Action.Execute` nodes disappear from 1.6-tagged cards without any error in
the log. Do not bump this without a host-side retest. Grep:

```
$ rg '"version"' src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates
```

All ten templates agree on `1.5`.

## Every action must carry `widgetId` + `verb`

Every `Action.Execute` and every container `selectAction` in the templates
carries both fields in its `data` block:

```json
"data": { "widgetId": "${widgetId}", "verb": "createNote" }
```

`${widgetId}` is bound from the top-level `widgetId` key that every
`CardDataBuilder.Build*Data` method sets at `_root.widgetId`. `verb` is both
the Adaptive Cards `verb` attribute (Host requirement) and an echoed `data`
key (so the dispatcher can read it post-`ParseInputs` without plumbing the
Host-level verb through). Missing either blanks the action server-side —
nothing dispatches — with no error.

## Where templates live

Source of truth: [`../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/)

Embedded via
[`../../src/ObsidianQuickNoteWidget.Core/ObsidianQuickNoteWidget.Core.csproj`](../../src/ObsidianQuickNoteWidget.Core/ObsidianQuickNoteWidget.Core.csproj):

```xml
<EmbeddedResource Include="AdaptiveCards\Templates\*.json" />
```

So any new `*.json` dropped in that folder ships automatically. Load them via
[`CardTemplates`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardTemplates.cs):

| Method | Resource |
| --- | --- |
| `CardTemplates.LoadForSize(size)` | `QuickNote.small.json` / `QuickNote.medium.json` / `QuickNote.large.json` (medium is the default for unknown sizes). |
| `CardTemplates.LoadPluginRunner(WidgetSize)` | `PluginRunner.{small,medium,large}.json`. |
| `CardTemplates.LoadPluginRunnerCustomize()` | `PluginRunner.customize.json`. |
| `CardTemplates.LoadPluginRunnerConfirmRemove()` | `PluginRunner.confirmRemove.json`. |
| `CardTemplates.Load(CardTemplates.RecentNotesTemplate)` | `RecentNotes.json`. |
| `CardTemplates.Load(CardTemplates.CliMissingTemplate)` | `CliMissing.json`. |

## `CardTemplates.Load*` ↔ `CardDataBuilder.Build*Data` pairs

`PushUpdate` ([`../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:568`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs))
always loads one template and builds exactly one data payload. The pairing:

| Context | Template loader | Data builder |
| --- | --- | --- |
| CLI missing | `Load(CliMissingTemplate)` | `CardDataBuilder.BuildCliMissingData(detail, widgetId)` |
| QuickNote (any size) | `LoadForSize(session.Size)` | `CardDataBuilder.BuildQuickNoteData(state, showAdvanced)` |
| RecentNotes | `Load(RecentNotesTemplate)` | `CardDataBuilder.BuildQuickNoteData(state, showAdvanced)` *(shares the QuickNote data shape; the template reads only `recentNotes` + `hasRecents` + `widgetId`)* |
| PluginRunner (grid) | `LoadPluginRunner(size)` | `CardDataBuilder.BuildPluginRunnerData(state, catalog, size)` |
| PluginRunner (customize) | `LoadPluginRunnerCustomize()` | `CardDataBuilder.BuildPluginRunnerCustomizeData(state, catalog, newLabelEcho, newCommandIdEcho)` |
| PluginRunner (confirm remove) | `LoadPluginRunnerConfirmRemove()` | `CardDataBuilder.BuildPluginRunnerConfirmData(state, catalog)` |

## Card input id table

**The contract:** every `${$root.inputs.<id>}` binding (and every `${…}`
reference at any level) must have a matching field set by the
`CardDataBuilder` method that pairs with that template. Violations cause the
N13-class lost-input bugs (typed text wiped on every re-render). The v1
card-author audit hit this; the 1.0.0.3 release fixed the last known case
(`folderNew` + LastFolder precedence).

The tables below enumerate **every** binding the Widget Host renderer sees,
its source template, and the exact `CardDataBuilder` line that supplies it.

### QuickNote.small.json

| Template binding | Kind | CardDataBuilder field | Note |
| --- | --- | --- | --- |
| `${$root.inputs.title}` | `Input.Text` value | `inputs.title = ""` ([`CardDataBuilder.cs:25`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) | Always cleared after create. |
| `${$root.statusMessage}` | `TextBlock` text | `statusMessage = RenderStatus(...)` ([`:36`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) | `s.LastError` wins, else `s.LastStatus`, else empty. |
| `${$root.statusColor}` | `TextBlock` color | `statusColor` ([`:37`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) | `"Attention"` on error, `"Good"` on status, `"Default"` otherwise. |
| `${$root.hasStatus}` | `isVisible` | `hasStatus` ([`:38`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) | |
| `${widgetId}` (in `data`) | action-data | `widgetId = s.WidgetId` ([`:22`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) | |

### QuickNote.medium.json

Inherits everything above, adds:

| Template binding | Kind | CardDataBuilder field |
| --- | --- | --- |
| `${$root.inputs.folder}` | `Input.ChoiceSet` value | `inputs.folder = s.LastFolder ?? ""` ([`:26`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${$root.folderChoices}` (array) | `ChoiceSet.choices.$data` | `folderChoices` ([`:35`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) via `BuildFolderChoices` — `(vault root)` + pinned + recent + cached, deduped. Each element has `{title, value}`. |
| `${$root.inputs.folderNew}` | `Input.Text` value | `inputs.folderNew = s.LastFolderNew ?? ""` ([`:27`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) — **N13 lost-input critical** |
| `${$root.inputs.body}` | `Input.Text` value | `inputs.body = ""` ([`:28`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |

### QuickNote.large.json

Inherits everything above, adds:

| Template binding | Kind | CardDataBuilder field |
| --- | --- | --- |
| `${$root.inputs.tagsCsv}` | `Input.Text` value | `inputs.tagsCsv = s.TagsCsv ?? ""` ([`:29`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${$root.inputs.template}` | `Input.ChoiceSet` value | `inputs.template = s.Template ?? "Blank"` ([`:30`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${$root.inputs.autoDatePrefix}` | `Input.Toggle` value | `inputs.autoDatePrefix = s.AutoDatePrefix ? "true" : "false"` ([`:31`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${$root.inputs.openAfterCreate}` | `Input.Toggle` value | `inputs.openAfterCreate = ...` ([`:32`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${$root.inputs.appendToDaily}` | `Input.Toggle` value | `inputs.appendToDaily = ...` ([`:33`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${$root.showAdvanced}` | `isVisible` | `showAdvanced` ([`:39`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) — driven by session, not state (transient). |
| `${$root.advancedLabel}` | button `title` | `advancedLabel = "Hide advanced" / "Show advanced"` ([`:40`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |

### RecentNotes.json

| Template binding | Kind | CardDataBuilder field |
| --- | --- | --- |
| `${$root.widgetId}` | action-data (inside `$data` loop) | `widgetId` (root) |
| `${$root.recentNotes}` (array) | `Container.$data` | `recentNotes` ([`:41`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) via `BuildRecentNotes` — each element `{title, path, folder, hasFolder}`, capped at 8. |
| `${title}` / `${path}` / `${folder}` / `${hasFolder}` | per-element | set inside `BuildRecentNotes` ([`:285-291`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${$root.hasNoRecents}` | `isVisible` (empty-state text) | `hasNoRecents = s.RecentNotes.Count == 0` ([`:42`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${$root.statusMessage}` / `${$root.statusColor}` / `${$root.hasStatus}` | `TextBlock` | as in QuickNote |

### PluginRunner.small.json / PluginRunner.medium.json / PluginRunner.large.json

| Template binding | Kind | CardDataBuilder field |
| --- | --- | --- |
| `${widgetId}` / `${$root.widgetId}` | action-data | `widgetId` ([`:117`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${isEmpty}` | `isVisible` (empty state) | `isEmpty = visible.Count == 0` ([`:125`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${hasActions}` | `isVisible` (row 0) | `hasActions = visible.Count > 0` ([`:123`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${hasRow1}` | `isVisible` (row 1) | `hasRow1 = row1.Count > 0` ([`:124`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${$root.row0}` / `${$root.row1}` (arrays) | `Column.$data` | `row0` / `row1` ([`:106-113`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) — split by column-cap (small: 2 in row0; medium: 2×2; large: 3×2). |
| `${id}` / `${label}` / `${commandId}` / `${lastResult}` | per-action | set by `ActionToJson` ([`:237-243`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)). `lastResult` is `"ok"`/`"error"` only when `state.LastRunResult.ActionId` matches; else `"none"`. |

Grid slot caps, authoritative in [`CardDataBuilder.cs:92-97`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs): small → (2, 2); medium → (4, 2); large → (6, 3).

### PluginRunner.customize.json

| Template binding | Kind | CardDataBuilder field |
| --- | --- | --- |
| `${widgetId}` / `${$root.widgetId}` | action-data | `widgetId` ([`:165`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${hasStatus}` / `${statusMessage}` / `${statusColor}` | `TextBlock` | `hasStatus` / `statusMessage` / `statusColor` ([`:174-176`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${isEmpty}` / `${hasItems}` | `isVisible` | `isEmpty` / `hasItems` ([`:167-168`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${$root.items}` (array) | `ColumnSet.$data` | `items` ([`:147`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) — each `{id, label, commandId, isPinned, isUnpinned}`. |
| `${id}` / `${label}` / `${commandId}` / `${isPinned}` / `${isUnpinned}` | per-item | [`:153-158`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs) |
| `${inputs.newLabel}` | `Input.Text` value | `inputs.newLabel = newLabelEcho` ([`:171`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${inputs.newCommandId}` | `Input.Text` value | `inputs.newCommandId = newCommandIdEcho` ([`:172`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |

### PluginRunner.confirmRemove.json

| Template binding | Kind | CardDataBuilder field |
| --- | --- | --- |
| `${widgetId}` | action-data | `widgetId` ([`:207`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${actionId}` | action-data | `actionId = target?.Id.ToString() ?? ""` ([`:208`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${label}` / `${commandId}` | `TextBlock` | `label` / `commandId` ([`:209-210`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${hasTarget}` | `isVisible` | `hasTarget = target is not null` ([`:211`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) — false when `PendingRemoveId` no longer maps to a catalog entry. |

### CliMissing.json

| Template binding | Kind | CardDataBuilder field |
| --- | --- | --- |
| `${widgetId}` | action-data | `widgetId` ([`BuildCliMissingData :53`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${$root.detail}` | `TextBlock` | `detail` ([`:54`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |
| `${$root.hasDetail}` | `isVisible` | `hasDetail` ([`:55`](../../src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs)) |

## Verbs

Every `Action.Execute.data.verb` string is dispatched by
[`ObsidianWidgetProvider.HandleVerbAsync`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs)
(for QuickNote / RecentNotes) or
[`PluginRunnerHandler.HandleVerbAsync`](../../src/ObsidianQuickNoteWidget/Providers/PluginRunnerHandler.cs)
(Plugin Runner; dispatched when `session.DefinitionId == PluginRunnerDefinitionId`).

### QuickNote / RecentNotes

| Verb | `data` shape | Handler |
| --- | --- | --- |
| `createNote` | `{ widgetId, verb, title, folder, folderNew, body, tagsCsv, template, autoDatePrefix, openAfterCreate, appendToDaily }` (input ids merged in) | `CreateNoteAsync` ([`ObsidianWidgetProvider.cs:300`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs)) |
| `pasteClipboard` | `{ widgetId, verb }` | inline ([`:266`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs)) — sets `session.PendingBodyPaste` from clipboard, status banner. |
| `toggleAdvanced` | `{ widgetId, verb }` | inline ([`:280`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs)) — flips `session.ShowAdvanced`. |
| `recheckCli` | `{ widgetId, verb }` | inline ([`:283`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs)) — no-op; next `PushUpdate` re-evaluates `_cli.IsAvailable`. |
| `openRecent` | `{ widgetId, verb, path }` | `HandleOpenRecentAsync` ([`:402`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs)) — routes through `IObsidianLauncher.LaunchNoteAsync`. |
| `openVault` | `{ widgetId, verb }` | inline ([`:288`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs)) — routes through `IObsidianLauncher.LaunchVaultAsync` (1.0.0.7; prior releases were a no-op when Obsidian was closed). |

### Plugin Runner

| Verb | `data` shape | Handler |
| --- | --- | --- |
| `runAction` | `{ widgetId, verb, actionId }` | `RunActionAsync` ([`PluginRunnerHandler.cs:133`](../../src/ObsidianQuickNoteWidget/Providers/PluginRunnerHandler.cs)) |
| `openCustomize` | `{ widgetId, verb }` | inline ([`:85`](../../src/ObsidianQuickNoteWidget/Providers/PluginRunnerHandler.cs)) |
| `cancelCustomize` | `{ widgetId, verb }` | inline ([`:90`](../../src/ObsidianQuickNoteWidget/Providers/PluginRunnerHandler.cs)) |
| `addAction` | `{ widgetId, verb, newLabel, newCommandId }` | `AddActionAsync` ([`:185`](../../src/ObsidianQuickNoteWidget/Providers/PluginRunnerHandler.cs)) |
| `removeActionConfirm` | `{ widgetId, verb, actionId }` | inline ([`:97`](../../src/ObsidianQuickNoteWidget/Providers/PluginRunnerHandler.cs)) — sets `state.PendingRemoveId`. |
| `removeAction` | `{ widgetId, verb, actionId }` | `RemoveActionAsync` ([`:225`](../../src/ObsidianQuickNoteWidget/Providers/PluginRunnerHandler.cs)) |
| `cancelRemove` | `{ widgetId, verb }` | inline ([`:107`](../../src/ObsidianQuickNoteWidget/Providers/PluginRunnerHandler.cs)) |
| `pinAction` | `{ widgetId, verb, actionId }` | inline ([`:111`](../../src/ObsidianQuickNoteWidget/Providers/PluginRunnerHandler.cs)) |
| `unpinAction` | `{ widgetId, verb, actionId }` | inline ([`:115`](../../src/ObsidianQuickNoteWidget/Providers/PluginRunnerHandler.cs)) |

Unknown verbs are logged at WARN and dropped. The Plugin-Runner handler
additionally traps all exceptions and surfaces them via `state.LastError` so
the COM boundary never sees a throw (defence in depth — B3-style).

## Editing a template without breaking the contract

1. Add the `${…}` binding in the JSON.
2. Add the matching field in the paired `CardDataBuilder.Build*Data` method.
3. Update the table above.
4. Add a `CardTemplatesTests` assertion that the placeholder exists (see the
   `CardTemplatesTests_TemplateContains_folderNew` pattern).
5. Build — `TreatWarningsAsErrors=true` won't catch the drift, but
   `dotnet test` will if you also wrote a round-trip binding test.

## See also

- [`architecture.md`](./architecture.md) — where `PushUpdate` and the
  dispatcher live.
- [`cli-surface.md`](./cli-surface.md) — what the verbs end up shelling out to.
- [`testing.md`](./testing.md) — how to assert a template ↔ data-builder pair.

Up: [`../README.md`](../README.md)
