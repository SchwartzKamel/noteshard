# Obsidian Quick Note Widget

> **Windows 11 only.** A Widget Board provider + tray companion that creates [Obsidian](https://obsidian.md) notes without ever leaving your current app. Built on .NET 10 and shipped as an MSIX.

Pin a Quick Note card to the Widget Board, type a title (or a full body with tags and template), hit **Create**, done. Or use the tray companion's global **Ctrl+Alt+N** hotkey for keyboard-first capture from anywhere in Windows.

No plugins. No HTTP server. No vault path configuration. The widget talks to Obsidian through the official `obsidian` CLI that ships with **Obsidian 1.12+**.

---

## What's in the box

Three pinnable widgets, all driven by the same MSIX:

- **Quick Note** — compose and create a note in Small / Medium / Large sizes. Folder dropdown, free-text "new folder", body, tags, template picker, and toggles for auto-date prefix / open-after-create / append-to-daily.
- **Recent Notes** — a live list of the most recently opened notes. Click any item to open it (launches Obsidian if closed).
- **Plugin Runner** — one-tap buttons that execute any Obsidian command by ID (e.g. `workspace:new-tab`, or anything your installed plugins expose). 2 / 4 / 6 tiles per size.

Plus a tray companion (`ObsidianQuickNoteTray.exe`) that registers **Ctrl+Alt+N** globally and opens a popup form using the same create pipeline.

---

## Install (end users)

Requires **Windows 11 22H2** (build 22621) or newer and **Obsidian 1.12+** with the CLI registered (*Obsidian → Settings → General → Command Line Interface → Enable → Register CLI*).

While the package is pre-release, install the signed MSIX directly:

```powershell
Add-AppxPackage -Path .\ObsidianQuickNoteWidget_<version>_x64.msix
```

Open the Widget Board (`Win + W`), click **+ Add widgets**, and pin one of the three Obsidian widgets.

Full install walkthrough: [`docs/users/getting-started.md`](docs/users/getting-started.md).

---

## Documentation

The [`docs/`](docs/) directory is split by audience. Each doc declares its intended reader at the top.

| If you want to… | Go to |
| --- | --- |
| **Use** the widget | [`docs/users/getting-started.md`](docs/users/getting-started.md) |
| **Customize** Plugin Runner actions | [`docs/users/widgets/plugin-runner.md`](docs/users/widgets/plugin-runner.md) |
| **Troubleshoot** — widget won't pin, CLI not found, ghost notes | [`docs/users/troubleshooting.md`](docs/users/troubleshooting.md) |
| **Build, test, or contribute** | [`docs/contributing/development.md`](docs/contributing/development.md) |
| **Understand the architecture** (COM server, widget definitions, state) | [`docs/contributing/architecture.md`](docs/contributing/architecture.md) |
| **Write Adaptive Card templates** | [`docs/contributing/adaptive-cards.md`](docs/contributing/adaptive-cards.md) |
| **Cut a release** | [`docs/contributing/release.md`](docs/contributing/release.md) |
| **Work on this repo as an AI agent** | [`docs/agents/README.md`](docs/agents/README.md) |
| **Report a vulnerability** | [`SECURITY.md`](SECURITY.md) |
| See what changed per release | [`CHANGELOG.md`](CHANGELOG.md) |

The full index with every doc is at [`docs/README.md`](docs/README.md).

---

## Quick build

From the repo root (PowerShell):

```powershell
dotnet restore ObsidianQuickNoteWidget.slnx
dotnet build   ObsidianQuickNoteWidget.slnx -c Release --nologo
dotnet test    -c Release --nologo
```

Package a signed MSIX and install it locally — the full pipeline, dev-cert bootstrap, and signtool invocation are in [`docs/contributing/development.md`](docs/contributing/development.md).

---

## Project layout

```
src/
  ObsidianQuickNoteWidget.Core/        Portable library — CLI wrapper, note creation, state,
                                        Adaptive Card templates, validators. All tested here.
  ObsidianQuickNoteWidget/              Windows-only COM server + MSIX package.
  ObsidianQuickNoteWidget.Tray/         WinForms tray app with the global hotkey.
tests/
  ObsidianQuickNoteWidget.Core.Tests/   xUnit tests for Core.
  ObsidianQuickNoteWidget.Tests/        xUnit tests for widget-assembly internals.
tools/
  New-DevCert.ps1, Sign-DevMsix.ps1     Dev signing helpers (see docs/contributing/development.md).
  AppExtProbe/                          Diagnostic for AppExtensionCatalog enumeration.
docs/                                   Audience-split documentation (see docs/README.md).
winget/                                 winget manifest (pre-publish).
```

The architecture doc walks through why the widget uses `MarshalInspectable<IWidgetProvider>` instead of a classic CCW, how the three widget definitions share one COM class, and where per-widget state lives: [`docs/contributing/architecture.md`](docs/contributing/architecture.md).

---

## License

MIT — see [`LICENSE`](LICENSE).

## Acknowledgements

- [Obsidian](https://obsidian.md) for the excellent knowledge-base app and the first-party CLI that made this possible.
- The [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/) Widgets API team.
