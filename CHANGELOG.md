# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- CLI exit-code success detection — now parses `Created:` / `Overwrote:` / `Added to:` / `Error:` prefixes on stdout (audit `cli-probe`).
- `CreateNoteAsync` now returns the CLI-reported path, honoring auto-rename on collision (audit `cli-probe`).
- Per-widget async lock around all `Get→mutate→Save` sequences in `ObsidianWidgetProvider` (bug-hunter B1).
- Timer no longer resurrects deleted widgets (bug-hunter B2).
- Fire-and-forget handlers now catch, log, and surface `LastError` to state (bug-hunter B3).
- `IsComServerMode` now correctly returns true only on `-Embedding`/`/Embedding` (widget-plumber).
- Card templates downgraded to Adaptive Cards 1.5; `widgetId` threaded into every action data block (card-author).
- Folder ChoiceSet switched from `compact` to `expanded` on medium+large so "type new or pick" works.
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
