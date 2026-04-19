[← docs index](../README.md)

# Agent onboarding

This page is for AI coding agents making changes to the Obsidian Quick Note Widget repo.

## What this repo is

- Windows 11 Widget Board provider packaged as MSIX, implemented as an out-of-proc COM server on .NET 10.
- Adaptive Cards UI rendered by the Widget Host; all Obsidian I/O goes through the official `obsidian` CLI (1.12+), with a URI-scheme launcher fallback for actions that must work when Obsidian is closed.
- Four projects: `ObsidianQuickNoteWidget.Core` (headless, tested), `ObsidianQuickNoteWidget` (COM server), `ObsidianQuickNoteTray` (WinForms tray + global hotkey), `tools/AppExtProbe` (diagnostic).

## Reading order (cold start)

1. `.github/copilot-instructions.md` — the extensive project prompt. **Read first.**
2. `docs/agents/conventions.md` — invariants + sharp edges. **Do not skip.**
3. `docs/agents/commands.md` — copy-paste commands for build / test / deploy / probe.
4. `src/ObsidianQuickNoteWidget/Program.cs` — STA + Win32 pump entry point.
5. `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs` — sole verb router.
6. `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/*.json` — form contracts.
7. `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs` — data bound into templates.
8. `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs` — CLI resolution + invocation.
9. `tests/ObsidianQuickNoteWidget.Core.Tests/` — `PerWidgetGateTests`, `ObsidianCliResolutionTests`, `CardTemplatesTests`, `CardDataBuilderTests`.

## Entry points for common tasks

| Task | Start here |
| --- | --- |
| Add a new verb to a widget | `docs/contributing/adaptive-cards.md` + `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs` (`HandleVerbAsync`, ~line 261) |
| Wrap a new Obsidian CLI verb | `docs/contributing/cli-surface.md` + `src/ObsidianQuickNoteWidget.Core/Cli/IObsidianCli.cs` + `ObsidianCli.cs` + `ObsidianCliParsers.cs` |
| Add a new widget definition | `src/ObsidianQuickNoteWidget/Package.appxmanifest` (`<Definition>`) + `src/ObsidianQuickNoteWidget/WidgetIdentifiers.cs` + routing in `ObsidianWidgetProvider.PushUpdate` |
| Add a new Adaptive Card template | `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/*.json` + `CardTemplates.Load*` + `CardDataBuilder.Build*Data` |
| Change per-widget persisted state | `src/ObsidianQuickNoteWidget.Core/State/WidgetState.cs` + `JsonStateStore` (atomic write preserved) |
| Ship a release | `docs/contributing/release.md` |

## Tests

- 377 tests, all `Core` only. Run `dotnet test -c Release`. See `docs/agents/commands.md`.
- No UI / COM test harness. If you touch COM, manifest, or the message pump, validate by packaging + installing the MSIX and watching `%LocalAppData%\Packages\ObsidianQuickNoteWidget_*\LocalCache\Local\...\log.txt`.

## Don't

- Don't add `System.Windows.*` references to `ObsidianQuickNoteWidget.Core`.
- Don't spread `Process.Start("obsidian", ...)` calls — the CLI seam is `IObsidianCli`.
- Don't throw out of a COM entry point — see `conventions.md`.
- Don't bump versions without syncing all four locations — see `conventions.md`.
