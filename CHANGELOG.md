# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
