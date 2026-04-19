# Architecture

> This page is for contributors who need to reason about the COM server, the core library, and how state and data flow through a widget render.

Up: [`../README.md`](../README.md) (docs index)

## Top-level shape

The widget ships as an **MSIX-packaged out-of-process COM server**. One CLSID,
one class factory, one provider instance serves three widget definitions:

| CLSID (fixed) | Provider class | Widget definitions served |
| --- | --- | --- |
| `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` | `ObsidianWidgetProvider` | `ObsidianQuickNote`, `ObsidianRecentNotes`, `PluginRunner` |

The CLSID lives in three places and must stay identical across all of them:

- [`../../src/ObsidianQuickNoteWidget/WidgetIdentifiers.cs:8`](../../src/ObsidianQuickNoteWidget/WidgetIdentifiers.cs)
- [`../../src/ObsidianQuickNoteWidget/Package.appxmanifest:47`](../../src/ObsidianQuickNoteWidget/Package.appxmanifest) — `<com:Class Id="…" />`
- Same manifest line 66 — `<CreateInstance ClassId="…" />`

Widget definitions are dispatched inside the provider by
`WidgetContext.DefinitionId`. The three definition ids live in the same
`WidgetIdentifiers.cs` alongside the CLSID:

- `ObsidianQuickNote` (small/medium/large; `AllowMultiple="true"`)
- `ObsidianRecentNotes` (medium/large; `AllowMultiple="false"`)
- `PluginRunner` (small/medium/large; `AllowMultiple="true"`)

`ObsidianWidgetProvider.HandleVerbAsync`
([`../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:225`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs))
branches on `DefinitionId == PluginRunnerDefinitionId` before the
QuickNote/RecentNotes verb switch. `PushUpdate`
([`:568`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs))
does the same for template/data selection.

## Why `MarshalInspectable<IWidgetProvider>.FromManaged`, not a classic CCW

Widget Host queries for the **WinRT** `IWidgetProvider` IID. A classic COM CCW
returned from `Marshal.GetIUnknownForObject` does not support QI for the WinRT
IID, and Widget Host silently rejects the provider (no error surface — the
widget just never activates).

The class factory lives in
[`../../src/ObsidianQuickNoteWidget/Com/ClassFactory.cs:50`](../../src/ObsidianQuickNoteWidget/Com/ClassFactory.cs)
and hands back an `IInspectable` produced by CsWinRT:

```csharp
if (riid == typeof(IWidgetProvider).GUID || riid == IID_IUnknown)
{
    ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(_instance);
    return 0; // S_OK
}
```

The entry point in
[`../../src/ObsidianQuickNoteWidget/Program.cs`](../../src/ObsidianQuickNoteWidget/Program.cs)
is equally load-bearing:

- `[STAThread]` (`:15`).
- Native Win32 message pump: `GetMessageW` / `TranslateMessage` /
  `DispatchMessageW` (`:84`). Any managed `Wait` / `Task.Delay` on this thread
  deadlocks inbound COM calls and PLM kills the process with `MoAppHang`
  within ~5 s.
- `IsComServerMode(args)` accepts only `-Embedding` / `/Embedding` (`:104`).
  Any other argv exits with a user-friendly message.

Graceful shutdown is `PostThreadMessageW(tid, WM_QUIT, …)` from the
`ProcessExit` + `CancelKeyPress` handlers.

## Projects

Four csproj files, listed in
[`../../ObsidianQuickNoteWidget.slnx`](../../ObsidianQuickNoteWidget.slnx):

| Project | TFM | Role |
| --- | --- | --- |
| [`../../src/ObsidianQuickNoteWidget.Core`](../../src/ObsidianQuickNoteWidget.Core) | `net10.0` | Portable headless logic. CLI, note creation, sanitizers, validators, state store, card templates + data builder. The **only** project with non-trivial test coverage today. |
| [`../../src/ObsidianQuickNoteWidget`](../../src/ObsidianQuickNoteWidget) | `net10.0-windows10.0.26100.0`, `WindowsPackageType=MSIX`, `WindowsAppSDKSelfContained=true` | Widget COM server. `IWidgetProvider` / `IWidgetProvider2` implementation, class factory, Ole32 P/Invokes, widget + Plugin-Runner dispatch. |
| [`../../src/ObsidianQuickNoteTray`](../../src/ObsidianQuickNoteTray) | `net10.0-windows` (WinForms) | Tray companion with global `Ctrl+Alt+N` hotkey → popup that reuses `Core.NoteCreationService`. |
| [`../../tests/ObsidianQuickNoteWidget.Core.Tests`](../../tests/ObsidianQuickNoteWidget.Core.Tests) | `net10.0` | xUnit. Portable. Mocks `IObsidianCli`, `IStateStore`, `ILog`. |
| [`../../tests/ObsidianQuickNoteWidget.Tests`](../../tests/ObsidianQuickNoteWidget.Tests) | `net10.0-windows10.0.26100.0` (x64) | xUnit against the widget assembly via `InternalsVisibleTo` (added in 1.0.0.3 to unblock the v3 test-author Top-1 `folderNew` coverage). |

There is also a small diagnostics binary at
[`../../tools/AppExtProbe`](../../tools/AppExtProbe) that enumerates
`AppExtensionCatalog.Open("com.microsoft.windows.widgets")` so you can verify
the OS sees the widget registration, bypassing Widget Host's cache.

## State flow

Per-widget state is persisted as JSON at
`%LocalAppData%\ObsidianQuickNoteWidget\state.json` (a single dictionary of
`widgetId → WidgetState`).

- Shape: [`../../src/ObsidianQuickNoteWidget.Core/State/WidgetState.cs`](../../src/ObsidianQuickNoteWidget.Core/State/WidgetState.cs) —
  size, LastFolder, LastFolderNew, toggles, caches (`CachedFolders`,
  `RecentNotes`, `PinnedFolders`), Plugin-Runner fields (`IsCustomizing`,
  `PendingRemoveId`, `PinnedActionIds`, `LastRunResult`).
- Store: [`../../src/ObsidianQuickNoteWidget.Core/State/JsonStateStore.cs`](../../src/ObsidianQuickNoteWidget.Core/State/JsonStateStore.cs) —
  atomic file write via `.tmp` + `File.Move`, in-process `Lock`. Cross-process
  (widget ↔ tray) is **last-write-wins** at the file layer; callers that need
  atomicity layer a per-id async mutex on top.
- Gate: [`../../src/ObsidianQuickNoteWidget.Core/Concurrency/AsyncKeyedLock.cs`](../../src/ObsidianQuickNoteWidget.Core/Concurrency/AsyncKeyedLock.cs) —
  one `SemaphoreSlim` per key (case-insensitive). Every `Get → mutate → Save`
  sequence in the provider runs under `_gate.WithLockAsync(widgetId, …)`
  (B1 fix in 1.0.0.1).

Why two layers: the store's `Lock` protects the file; the `AsyncKeyedLock`
protects the logical transaction (timer ticks, fire-and-forget refreshes, and
COM callbacks can all target the same widget simultaneously).

## CLI and URI launcher split

Two disjoint seams talk to Obsidian:

| Seam | Interface | Requires Obsidian running? | Used for |
| --- | --- | --- | --- |
| Native CLI | [`IObsidianCli`](../../src/ObsidianQuickNoteWidget.Core/Cli/IObsidianCli.cs) (impl: [`ObsidianCli`](../../src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs)) | **Yes** | `vault info=path`, `folders`, `files`, `recents`, `create`, `open`, `daily:append`, `command`, `commands`. All reads + note creation. |
| URI scheme | [`IObsidianLauncher`](../../src/ObsidianQuickNoteWidget.Core/Cli/IObsidianLauncher.cs) (impl: [`ObsidianLauncher`](../../src/ObsidianQuickNoteWidget.Core/Cli/ObsidianLauncher.cs)) | **No** — launches Obsidian if closed | `openVault`, `openRecent`. 1.0.0.7 added this because the bundled CLI's `open` verb is a no-op when Obsidian isn't running. |

`ObsidianLauncher` resolves the active vault from
`%APPDATA%\obsidian\obsidian.json` (prefer `"open": true`, else newest `ts`,
else first), URL-encodes components, and shells `obsidian://open?vault=…&file=…`.
`IsSafeRelativePath` rejects control chars, rooted paths, and `..` segments
before handing a value to the shell (v7 threat model — see
[`security.md`](./security.md)).

See [`cli-surface.md`](./cli-surface.md) for every verb we use and its
verified stdout shape.

## Data flow: a createNote click

```
┌──────────┐  Action.Execute.data     ┌──────────────┐  IWidgetProvider  ┌────────────────────────┐
│  Card    ├─────────────────────────▶│ Widget Host  ├──────────────────▶│ ObsidianWidgetProvider │
└──────────┘  { verb, widgetId, ... } └──────────────┘  OnActionInvoked  └────────┬───────────────┘
                                                                                  │ (dispatch by verb
                                                                                  │  under per-id gate)
                                                                                  ▼
                                                                        ┌─────────────────────┐
                                                                        │  CreateNoteAsync    │
                                                                        │  → NoteCreationSvc  │
                                                                        │  → IObsidianCli     │
                                                                        │    create name=...  │
                                                                        └────────┬────────────┘
                                                                                 │  status + path
                                                                                 ▼
                                                                        ┌─────────────────────┐
                                                                        │  WidgetState mutate │
                                                                        │  + JsonStateStore   │
                                                                        └────────┬────────────┘
                                                                                 │
                                                                                 ▼
                                                                        ┌─────────────────────┐
                                                                        │  PushUpdate         │
                                                                        │  = Template + Data  │
                                                                        │  → UpdateWidget()   │
                                                                        └─────────────────────┘
```

The salient moments, in code:

1. **Card action-data** includes `widgetId` and `verb` on every
   `Action.Execute.data` (and every `selectAction.data`) — missing either is
   the N13 lost-input repro (see [`adaptive-cards.md`](./adaptive-cards.md)).
2. **`OnActionInvoked`** ([`ObsidianWidgetProvider.cs:109`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs)) logs and schedules
   `HandleVerbAsync` via `FireAndLog`, which runs through `AsyncSafe.RunAsync`
   — it never throws out to the COM boundary (B3).
3. **Per-id gate** (`_gate.WithLockAsync`) wraps the `Get → mutate → Save` so
   a concurrent timer tick or paste action can't clobber the result.
4. **`NoteCreationService.CreateAsync`** ([`../../src/ObsidianQuickNoteWidget.Core/Notes/NoteCreationService.cs`](../../src/ObsidianQuickNoteWidget.Core/Notes/NoteCreationService.cs))
   sanitizes title, validates folder (`FolderPathValidator`), composes
   frontmatter, resolves duplicate filenames, and shells `obsidian create`.
5. **`IObsidianCli.CreateNoteAsync`** returns the CLI-reported path (parsed by
   `ObsidianCliParsers.TryParseCreated` — `Created:` / `Overwrote:` prefixes).
   Errors surface via `HasCliError("Error:" prefix)` because the CLI returns
   exit=0 on all outcomes (1.0.0.1 fix).
6. **State update:** `LastFolder` is persisted **only on success** (N15),
   newly-typed folders are optimistically added to `CachedFolders` (N14),
   and `LastFolderNew` is cleared on success or preserved on failure so the
   user doesn't lose their text (N13).
7. **`PushUpdate`** rebuilds the card: template via `CardTemplates.LoadForSize`
   (or `LoadPluginRunner` / `RecentNotesTemplate` / `CliMissingTemplate`),
   data via `CardDataBuilder.BuildQuickNoteData` (or the Plugin-Runner / CLI-missing variants),
   then `WidgetManager.GetDefault().UpdateWidget(options)`.

The same pattern applies to every verb — dispatch → optional CLI call →
state mutation under gate → `PushUpdate`. See
[`adaptive-cards.md`](./adaptive-cards.md#verbs) for the full verb → handler
table.

## Background work

| Source | Frequency | Site |
| --- | --- | --- |
| Folder-cache refresh (all active widgets) | every 2 min | `_folderRefreshTimer` → `RefreshAllActiveAsync` ([`ObsidianWidgetProvider.cs:66`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs)) |
| Folder-cache refresh (single widget) | on `CreateWidget`, on `Activate`, after successful `createNote` | `RefreshFolderCacheAsync` |
| Recent-notes refresh | 30 s TTL on RecentNotes widget renders | `RefreshRecentNotesAsync` ([`ObsidianWidgetProvider.cs:460`](../../src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs)) — runs `recents` + `files` concurrently, intersects, dedupes, caps at 16 (1.0.0.6 ghost-entry fix). |

All background paths go through `FireAndLog`, which traps exceptions, writes
`LastError` to state, and fires a `PushUpdate` so the user sees the failure
instead of a silent hang.

## See also

- [`adaptive-cards.md`](./adaptive-cards.md) — card templates + data bindings.
- [`cli-surface.md`](./cli-surface.md) — verified `obsidian` verbs.
- [`testing.md`](./testing.md) — test layout.
- [`security.md`](./security.md) — threat model and F-series findings.

Up: [`../README.md`](../README.md)
