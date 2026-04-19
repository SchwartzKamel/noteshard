[← docs index](../README.md)

# Conventions and invariants

This page is for AI coding agents making changes to the Obsidian Quick Note Widget repo. Every item below is a load-bearing rule — breaking one silently regresses behavior that tests won't catch.

## Adaptive Cards

- **Pin schema `"version": "1.5"`** on every template under `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/*.json`. Widget Host's renderer **silently drops actions** on `"version": "1.6"`.
- Every `Action.Execute` / `Action.Submit` / `selectAction` **MUST** carry `data.widgetId` and `data.verb`. `widgetId` is templated as `"${widgetId}"` and supplied by `CardDataBuilder`. Verb routing is a `switch` in `ObsidianWidgetProvider.HandleVerbAsync`.
- **Every `${$root.inputs.<id>}` binding in a template MUST have a matching key in the `inputs` object of the corresponding `CardDataBuilder.Build*Data` method.** Missing keys bind to `undefined` → Adaptive Cards drops the `value` property → field blanks on every re-render (the **N13 lost-input precedent**; `folderNew` bit us). Add `["<id>"] = string.Empty` if you don't want it to persist.
- Template naming: `QuickNote.{small,medium,large}.json` are **all** QuickNote; `RecentNotes.json` / `PluginRunner.*.json` are their own widgets. Dispatch is in `ObsidianWidgetProvider.PushUpdate` on `session.DefinitionId`.
- Card size → template is `CardTemplates.LoadForSize`. The medium / large discriminator test strings in `CardTemplatesTests.cs` are brittle; update both sides when editing placeholders.

## COM / Widget Host

- **Never throw out of a COM entry point** (`CreateWidget`, `DeleteWidget`, `OnActionInvoked`, `OnWidgetContextChanged`, `Activate`, `Deactivate`). Route all async work through `AsyncSafe.RunAsync` (`Core/Concurrency/AsyncSafe.cs`) and `FireAndLog` (`Providers/ObsidianWidgetProvider.cs`). A bare throw crashes the COM host.
- **`[STAThread] Main` must pump native Win32 messages** — `GetMessageW` / `TranslateMessage` / `DispatchMessageW`. Any managed wait deadlocks Widget Host's inbound COM. See `src/ObsidianQuickNoteWidget/Program.cs` + `Com/Ole32.cs`.
- **`ClassFactory.CreateInstance` returns WinRT `IInspectable`** via `MarshalInspectable<IWidgetProvider>.FromManaged(_instance)`. `Marshal.GetIUnknownForObject` produces a classic CCW that fails QI for the WinRT IID and Widget Host drops the provider silently.
- **`GetCurrentThreadId` lives in `kernel32.dll`** (not `user32.dll`). Wrong P/Invoke loads fine, throws `EntryPointNotFoundException` at first call.
- **`<TargetDeviceFamily MinVersion ≥ 10.0.22621.0>`** in the manifest and `TargetPlatformMinVersion` in the csproj must match. Lower versions are silently filtered.
- Every `<Definition>` needs `<Screenshots>` + `<ThemeResources>` with at least empty `<DarkMode />` and `<LightMode />`, or it won't appear in the picker.

## Concurrency

- **`AsyncKeyedLock<string> _gate` gates all per-widget state mutations** (`Core/Concurrency/AsyncKeyedLock.cs`). Every read-modify-write on `WidgetState` runs under `_gate.WithLockAsync(widgetId, …)`. The lock is **in-process only**; cross-process atomicity (tray ↔ widget host) is achieved by id partitioning — the tray uses the literal key `"tray"`.
- `FireAndLog` / `AsyncSafe.RunAsync` write `LastError` under the gate and call `SafePushUpdate` on failure. Don't add a second `catch`-and-push in your verb handler.

## CLI surface

- **Invocation shape is positional `key=value`**, not `--flags`. Quote values with spaces: `name="My Note"`. See `Core/Cli/ObsidianCli.cs`.
- **`obsidian` exits 0 on errors.** The authoritative failure signal is a line on stdout beginning `Error:`. Detect with `ObsidianCliParsers.HasCliError(stdout)` — check this on every mutating call.
- **Use `ProcessStartInfo.ArgumentList`, never string-concat args** (F-01 precedent). Each token is a separate `ArgumentList.Add(...)`. Inside `content=` values, escape backslash first, then `\n` / `\t`, via `ObsidianCliParsers.EscapeContent`.
- **`obsidian ls` does not exist.** Folders come from `obsidian folders`. `obsidian recents` returns ghost entries for deleted files — **must be intersected with `obsidian files`** (see `ObsidianWidgetProvider.IntersectRecentsWithFiles`).
- **CLI resolution ladder** (`Core/Cli/ObsidianCli.cs`): `OBSIDIAN_CLI` env → `%ProgramFiles%\Obsidian\Obsidian.com` → `%LocalAppData%\Programs\Obsidian\Obsidian.com` → `HKCU\…\obsidian\shell\open\command` → PATH (`.com` / `.exe` only; `.cmd` / `.bat` are deliberately rejected — F-02).
- **Most verbs require Obsidian running** (the CLI IPCs into a live instance). For user-facing "open vault" / "open note" actions that must work when Obsidian is closed, use `IObsidianLauncher` (URI scheme — `obsidian://`), not `IObsidianCli`.

## Logging

- **Sanitize every user-provided string** passed to the log with `FileLog.SanitizeForLogLine` (F-03, defeats CRLF/C0 log-injection). `FileLog.Info/Warn/Error` already sanitize internally; don't pre-format multi-line blobs.
- Logger catches all write errors on purpose — a logger must never crash the widget.

## Versioning — four-way sync on every release

All four must match exactly — see `docs/contributing/release.md`:

1. `src/ObsidianQuickNoteWidget/Package.appxmanifest` → `<Identity … Version="X.Y.Z.W" />`.
2. `src/ObsidianQuickNoteWidget/Package.appxmanifest` → `<Application Executable="…" EntryPoint="…">` version (if set).
3. `winget/ObsidianQuickNoteWidget.yaml` → `PackageVersion:`.
4. `winget/ObsidianQuickNoteWidget.installer.yaml` → `PackageVersion:` + `InstallerUrl` tag.

Locale file (`winget/ObsidianQuickNoteWidget.locale.en-US.yaml`) also holds `PackageVersion:`.

## Identifier sync — four-way on the CLSID

The provider CLSID `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` appears at four points and **must stay in sync**:

1. `src/ObsidianQuickNoteWidget/WidgetIdentifiers.cs` → `ProviderClsid`.
2. `src/ObsidianQuickNoteWidget/Package.appxmanifest` → `<com:Class Id="…">`.
3. `src/ObsidianQuickNoteWidget/Package.appxmanifest` → `<CreateInstance ClassId="…">`.
4. Anywhere a consumer registers the COM server out-of-proc (none currently outside the manifest + `ClassFactory`).

Widget definition IDs (`ObsidianQuickNote`, `ObsidianRecentNotes`, `PluginRunner`) in `WidgetIdentifiers.cs` must match the `<Definition Id="…">` blocks in `Package.appxmanifest`.

## Testing

- All tests live in `tests/ObsidianQuickNoteWidget.Core.Tests/` and cover `Core` only. 377 tests at HEAD.
- `Core` exposes internals to tests via `<InternalsVisibleTo Include="ObsidianQuickNoteWidget.Core.Tests" />` in `Core.csproj`. The Windows widget assembly uses the same for any logic extracted to internal helpers.
- `TreatWarningsAsErrors=true` is global (`Directory.Build.props`). A stray `// TODO` comment or unused using fails the build.

## Widget Host caching

- Widget Host caches the `AppExtension` catalog (icon, sizes, definitions, CLSID) aggressively. After any change that touches widget **shape** (manifest definitions, sizes, CLSID, provider interfaces), the cache must be invalidated:
  - Kill `Widgets.exe`, `WidgetService.exe`, `WebExperienceHost.exe`, `dasHost.exe`, and any `ObsidianQuickNoteWidget.exe` — **or**
  - Elevated: `Add-AppxPackage -DisableDevelopmentMode -Register <path>\AppxManifest.xml` to fully re-register.
  - Nuclear: full `Remove-AppxPackage` + reinstall. `Add-AppxPackage -ForceApplicationShutdown -ForceUpdateFromAnyVersion` leaves stale metadata.
- Confirm the OS sees the new registration with `tools/AppExtProbe` (bypasses the Widget Host cache).

## Short Don't list

- Don't add `System.Windows.*` / WinForms / WinRT Windowing references to `ObsidianQuickNoteWidget.Core`.
- Don't spread `Process.Start("obsidian", ...)` calls — all shelling out goes through `IObsidianCli`.
- Don't persist unvalidated free-text folders without routing through `FolderPathValidator`.
- Don't pre-format log messages with embedded newlines — sanitizer will escape them.
- Don't use `Marshal.GetIUnknownForObject` in `ClassFactory`.
- Don't skip the `HasCliError` check on a mutating CLI call.
