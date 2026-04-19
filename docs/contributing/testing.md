# Testing

> This page is for contributors adding or reshaping tests. The suite is xUnit, the two projects have different rules, and there is one known structural blind spot worth understanding before you add a test in the wrong place.

Up: [`../README.md`](../README.md) (docs index)

## Layout

Two xUnit test projects, called out in
[`../../ObsidianQuickNoteWidget.slnx`](../../ObsidianQuickNoteWidget.slnx):

### [`../../tests/ObsidianQuickNoteWidget.Core.Tests`](../../tests/ObsidianQuickNoteWidget.Core.Tests)

- **Target:** [`../../src/ObsidianQuickNoteWidget.Core`](../../src/ObsidianQuickNoteWidget.Core) — portable `net10.0`, no Windows APIs.
- **Visibility:** `InternalsVisibleTo("ObsidianQuickNoteWidget.Core.Tests")`
  on the Core csproj, so `internal` helpers (`ObsidianCliParsers`, `FileLog.SanitizeForLogLine`, …) are fair game.
- **Scope:** everything that doesn't touch the widget COM assembly or
  `Microsoft.Windows.Widgets.Providers`. `ObsidianCli`, `NoteCreationService`,
  `FolderPathValidator`, `FilenameSanitizer`, `FrontmatterBuilder`,
  `DuplicateFilenameResolver`, `JsonStateStore`, `JsonActionCatalogStore`,
  `RunnerActionValidator`, `ObsidianCommandInvoker`, `ObsidianLauncher`,
  `CardTemplates`, `CardDataBuilder`, `AsyncSafe`, `AsyncKeyedLock`.

### [`../../tests/ObsidianQuickNoteWidget.Tests`](../../tests/ObsidianQuickNoteWidget.Tests)

- **Target:** [`../../src/ObsidianQuickNoteWidget`](../../src/ObsidianQuickNoteWidget) — widget COM assembly.
- **Platform:** `x64` only (`Platforms>x64</Platforms>`, `TargetPlatformMinVersion=10.0.22621.0`).
- **Visibility:** the widget csproj has
  `<InternalsVisibleTo Include="ObsidianQuickNoteWidget.Tests" />` — added in
  1.0.0.3 to unblock the v3 test-author Top-1 (`folderNew` precedence).
- **Scope:** provider-level logic that needs `internal` hooks, e.g.
  `ObsidianWidgetProvider.InvokeVerbForTest`, `RegisterActiveForTest`,
  `IntersectRecentsWithFiles`, `ShouldRefreshRecents`, and
  `PluginRunnerHandler.HandleVerbAsync`.

**Total at HEAD (`cbce283`, 1.0.0.7): 377 passed.**

### The Tray companion

[`../../src/ObsidianQuickNoteTray`](../../src/ObsidianQuickNoteTray) has no
dedicated test project. Its logic re-uses `Core.NoteCreationService` (already
covered) and a WinForms UI layer not worth unit-testing.

## Fakes and mocks

No mocking framework. Fakes are hand-rolled in
[`../../tests/ObsidianQuickNoteWidget.Core.Tests/`](../../tests/ObsidianQuickNoteWidget.Core.Tests/).

| Fake | What it stubs | Canonical use |
| --- | --- | --- |
| `FakeCli` (in `NoteCreationServiceTests`, `CliParsersTests`, etc.) | `IObsidianCli` | Programs scripted stdout lines per verb; records arguments for round-trip asserts (e.g. `CreatedBody`, `CreatedPath`). |
| Process runner stubs (via `IObsidianCliEnvironment`) | PATH lookup / file existence | `ObsidianCliResolutionTests` — injects a fake env to exercise the PATH → known-install → override resolution order without actually spawning `obsidian.exe`. |
| File reader stubs (via `ObsidianLauncher(internal)` ctor) | `File.Exists`, `File.ReadAllText`, `Process.Start` | `ObsidianLauncherTests` — pumps a canned `obsidian.json` through `ResolveVaultName`, asserts the generated `obsidian://` URI. |
| `FakeCatalogStore` / `FakeCommandInvoker` | `IActionCatalogStore`, `IObsidianCommandInvoker` | `PluginRunnerHandlerTests` — drives the Plugin Runner verb dispatcher without touching the filesystem or the CLI. |

All fakes are test-internal and share the Core.Tests assembly's
`InternalsVisibleTo` seam into Core — they construct Core types directly.

## Running

```powershell
# full suite
dotnet test -c Release

# one project
dotnet test tests/ObsidianQuickNoteWidget.Core.Tests -c Release

# one test class
dotnet test --filter "FullyQualifiedName~FilenameSanitizerTests"

# one test method
dotnet test --filter "FullyQualifiedName~PluginRunnerHandlerTests.AddAction_InvalidLabel_SurfacesError"

# substring match on display name
dotnet test --filter "DisplayName~PluginRunner"

# watch loop (Core only)
dotnet watch --project tests/ObsidianQuickNoteWidget.Core.Tests test
```

`--no-build` skips the compile step and is ~5 s faster on re-runs. `make test`
wraps the Core-only path; there is no make target for the widget tests today.

## Coverage blind spots

Known uncovered surfaces — the short list at HEAD:

- **`NoteCreationService.BuildBody`** seeded-vs-user composition (separator
  `\n\n`, `TrimEnd()`, template seeding arm) — v2 Top-1, still partial.
- **`FileLog.Roll`** 1 MB boundary + unwritable-file swallow — no test
  pre-seeds past `MaxBytes`; the strict `>` vs `>=` boundary survives
  mutation.
- **Provider input-plumbing helpers** (`ObsidianWidgetProvider.ParseInputs`,
  `ParseBool`, `RememberRecent`) — `private`, not `internal`, so even with
  the widget test project they require an extra `InternalsVisibleTo`-friendly
  pass or accessor seam to exercise directly.
- **`ObsidianWidgetProvider.CreateNoteAsync` fallback arms** — the
  `Enum.TryParse<NoteTemplate>` `ignoreCase` fallback to `Blank`, and
  `state.Template = template ?? "Blank"` persisted-fallback. Both covered
  only transitively today.

Close the gap by preference order: pure Core function → Core integration with
fakes → `InvokeVerbForTest` through the provider → new `InternalsVisibleTo`
surface.

## See also

- [`architecture.md`](./architecture.md) — where the seams are.
- [`adaptive-cards.md`](./adaptive-cards.md) — template ↔ data-builder
  round-trip assertions live in `CardTemplatesTests` and
  `CardDataBuilderTests`.

Up: [`../README.md`](../README.md)
