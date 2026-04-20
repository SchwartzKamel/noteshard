# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0.9] - 2026-04-19

### Fixed
- **Typed-text preservation** — Quick Note no longer wipes the title/body/folder you are mid-composing when the Widget Board activates, resizes, or the 2-minute folder-cache timer ticks in the background. The Windows 11 Widget Host resets all `Input.*` values on every `UpdateWidget` call, so `ObsidianWidgetProvider` now suppresses `PushUpdate` on background paths (`Activate`, focus/visibility `OnWidgetContextChanged` transitions, and `RefreshAllActiveAsync` timer refreshes) and only pushes on explicit user actions, size changes, first-pin, and post-create refreshes where the inputs are already cleared.

## [1.0.0.8] - 2026-04-19

### Security
- **F-04** (MED) — `FolderPathValidator` now rejects leading-dot segments (e.g. `.obsidian`, `.git`, `.trash`), closing the write-into-config-tree vector opened by the `folderNew` free-text input.
- **F-05** (LOW) — Removed the `%UserProfile%\ObsidianWidget-proof.log` proof-of-life writer from `Program.Main`. Widget activation is covered by the structured `FileLog` under `%LocalAppData%`.
- **F-06** (LOW) — `JsonStateStore.Load` now caps `state.json` at 1 MB. Oversized files are quarantined as `state.json.oversized.<yyyyMMddHHmmss>` and the store degrades to empty state.
- **F-07** (LOW) — `JsonStateStore.Load`/`Persist` replaced bare `catch` with scoped handlers (`JsonException`/`IOException`/`UnauthorizedAccessException`) that log via `FileLog`. Corrupt state is quarantined as `state.json.corrupt.<yyyyMMddHHmmss>` for user recovery.
- **F-08** (LOW) — `%LocalAppData%\ObsidianQuickNoteWidget\` is now created with inheritance disabled and owner-only `FullControl` via new `DirectorySecurityHelper.CreateWithOwnerOnlyAcl`. Called from both `FileLog` and `JsonStateStore` constructors; idempotent per process, best-effort on failure.
- **F-12** (LOW) — `OBSIDIAN_CLI` override now rejects UNC / device-namespace paths (`\\server\share`, `\\?\...`, `\\.\...`), relative paths, and reparse-point targets (symlink / directory junction). Rejection logs a one-shot sanitized warning and falls through to the next resolver.
- **F-16** (LOW) — `FolderPathValidator` now rejects C0 control characters (`\0`, `\r`, `\n`, `\t`, `\x01`..`\x1F`, `\x7F`) per segment; error messages pass through `FileLog.SanitizeForLogLine` so rejected payloads can't re-introduce log injection.

### Added
- `ObsidianQuickNoteWidget.Core.IO.DirectorySecurityHelper` (internal) — creates user-scoped data directories with tightened Windows ACLs.

## [1.0.0.7] - 2026-04-19

### Fixed
- "Open vault" button now launches Obsidian if it's closed (previously a no-op because the CLI requires Obsidian to already be running).
- "Open recent note" now also launches Obsidian when closed, via the registered `obsidian://` URI scheme.

### Added
- `IObsidianLauncher` service + `ObsidianLauncher` implementation; vault auto-discovery from `%APPDATA%/obsidian/obsidian.json` with `OBSIDIAN_VAULT` env override.

## [1.0.0.6] - 2026-04-19

### Fixed
- Recent Notes widget no longer shows ghost entries for deleted files. `obsidian recents` returns historical paths; we now intersect with the live `obsidian files` list so only currently-present .md files render.

## [1.0.0.5] - 2026-04-19

### Fixed
- Recent Notes widget now populates from the vault via `obsidian recents` CLI (previously always empty because the per-widget state was never written to by that widget).

### Added
- `ListRecentsAsync` on IObsidianCli; 30s TTL refresh cache on RecentNotes widgets; parent-folder subtitle on each recent entry.

## [1.0.0.4] - 2026-04-19

### Added
- **Plugin Runner widget** — third widget definition with one-tap buttons that execute any Obsidian command via `obsidian command id=...`. Small (2 tiles), Medium (4), Large (6).
- Global action catalog persisted to `%LocalAppData%/ObsidianQuickNoteWidget/action-catalog.json` (survives widget unpin).
- Per-widget `PinnedActionIds` so multiple runner widgets can show different action subsets.
- Customization card with add / remove (with confirmation) / pin / unpin verbs.
- `ObsidianCommandInvoker` wrapping the CLI `command` and `commands` verbs; detects `Error:`-prefixed failures on exit=0.

### Changed
- `WidgetState` gains `IsCustomizing`, `PendingRemoveId`, `LastRunResult`, `PinnedActionIds` (all default-empty/null — backward-compatible).

## [1.0.0.3] - 2026-04-19

### Fixed
- `folderNew` input value now persists across widget re-renders (was wiped on timer push / status swap) — N13
- Typing a new folder now adds it to the folder dropdown cache on success (was silently dropped) — N14
- `LastFolder` only persisted on successful create (was poisoned on CLI errors / validation rejections) — N15

### Security
- `FolderPathValidator` now rejects C0 control characters in folder segments — F-16

## [1.0.0.2] - 2026-04-19

### Changed
- Folder picker reverted from `expanded` back to `compact` `ChoiceSet.Input` on medium + large templates (the v2 `expanded` bullet was claimed but not shipped; this release makes the docs match reality).

### Added
- New `folderNew` `Input.Text` on medium + large QuickNote templates — type a new folder name to override the `folder` dropdown selection when creating a note.

### Fixed
- Manifest `Identity/@Version` bumped `1.0.0.1` → `1.0.0.2` so Widget Host picks up the template changes on reinstall.

## [1.0.0.1] - 2026-04-19

### Fixed
- CLI exit-code success detection — now parses `Created:` / `Overwrote:` / `Added to:` / `Error:` prefixes on stdout (audit `cli-probe`).
- `CreateNoteAsync` now returns the CLI-reported path, honoring auto-rename on collision (audit `cli-probe`).
- Per-widget async lock around all `Get→mutate→Save` sequences in `ObsidianWidgetProvider` (bug-hunter B1).
- Timer no longer resurrects deleted widgets (bug-hunter B2).
- Fire-and-forget handlers now catch, log, and surface `LastError` to state (bug-hunter B3).
- `IsComServerMode` now correctly returns true only on `-Embedding`/`/Embedding` (widget-plumber).
- Card templates downgraded to Adaptive Cards 1.5; `widgetId` threaded into every action data block (card-author).
- `<AdditionalTasks/>` added to both Definitions; manifest bumped to 1.0.0.1 (manifest-surgeon).
- Orphan `WidgetsDefinition.xml` deleted (doc-scribe, code-archaeologist).

### Security
- Dev-cert password is now generated per-developer and stored with user-only ACL; removed literal from docs (security-auditor F-01).
- Obsidian CLI resolution prefers known install locations; `.cmd`/`.bat` rejected from PATH fallback (F-02).
- Log lines sanitized against CRLF/control-char injection (F-03).

## [0.1.0] - initial scaffolding
- Windows 11 Widget Board provider (MSIX-packaged, out-of-proc COM).
- Quick-note create + recent-notes widgets.
- Tray helper with global hotkey.
- Obsidian CLI integration (`vault info=path`, `folders`, `create`, `open`, `daily:append`).
- Core/providers separation; xUnit suite with mutation-tested behaviors.
