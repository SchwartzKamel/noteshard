# Obsidian Quick Note Widget

A Windows 11 **Widget Board** provider that creates Obsidian notes without ever opening Obsidian — plus a tray companion with a global hotkey for keyboard-first capture.

Packaged as an out-of-proc COM server inside an MSIX, targeting .NET 10 on Windows 11 22H2+.

## What it does

- Pin **Obsidian Quick Note** from the Widget Board (⊞+W → +) and capture notes inline.
- Talks to Obsidian through the official `obsidian` CLI shipped with **Obsidian 1.12+** — no plugins, no HTTP server, no vault path configuration in the widget.
- Tray companion (`ObsidianQuickNoteTray`) registers **Ctrl+Alt+N** globally and opens a popup that reuses the same create pipeline.
- A separate **Recent Notes** widget can be pinned alongside to jump back into recently-created notes.

## Widget sizes

Each size is a distinct Adaptive Card template (`QuickNote.{small,medium,large}.json`) driven by `CardDataBuilder`.

| Size | Surface |
| --- | --- |
| **Small** | Title field + Create button. |
| **Medium** | Title + folder dropdown + *new-folder* text input + body + Paste / Create buttons. |
| **Large** | Full form: title, folder dropdown + *new-folder* text input, body, tags, template picker (Daily / Meeting / Book / Idea), and toggles for auto-date prefix / open-after-create / append-to-daily. |
| **Recent Notes** (separate widget) | Standalone pinnable Adaptive Card listing the most recently created notes; tap to open in Obsidian. |

On medium and large, the folder dropdown (compact `ChoiceSet.Input`, id `folder`) lists the vault's existing folders. Typing a name into the adjacent *new folder* text input (id `folderNew`) overrides the dropdown selection for that create — use it to drop a note into a folder that doesn't exist yet (or isn't in the cache). Leave `folderNew` blank to use the dropdown pick.

The folder list is cached and auto-refreshed every 2 minutes (and after every successful create).

## Obsidian CLI operations used

The widget shells out to `obsidian` for exactly five operations — everything else is pure C#:

- `obsidian vault info=path` — resolve the active vault root.
- `obsidian folders` — enumerate vault folders (with filesystem fallback).
- `obsidian create name=… path=… content=… [template=…] [overwrite] [open] [newtab]` — create the note.
- `obsidian open path=…` — optional open-after-create.
- `obsidian daily:append content=…` — the *append to daily* toggle.

Note: arguments are **positional `key=value` tokens**, not `--flags`. See `.github/copilot-instructions.md` for the full verified surface.

## Requirements

- **Windows 11 22H2** (build 22621) or later.
- **.NET 10 SDK** (to build from source; end users of a signed MSIX don't need the SDK — the widget is published self-contained).
- **Obsidian 1.12+** with the CLI registered on `PATH`: *Obsidian → Settings → General → Command Line Interface → Enable → Register CLI*.
- **Developer Mode** enabled (Settings → Privacy & security → For developers) for sideloading dev builds.

## Build, test, package

From the repo root:

```powershell
make build      # dotnet restore + dotnet build -c Debug
make test       # dotnet test on the Core.Tests project
make pack       # dotnet publish -c Release producing the MSIX
```

Plain `dotnet` commands also work — see `.github/copilot-instructions.md` for the exact invocations the Make targets wrap.

### Dev cert + signing

Sideloaded MSIX builds need a trusted dev cert. Generate one on first use:

```powershell
.\tools\New-DevCert.ps1
```

This creates `%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\dev.pfx` (CN `ObsidianQuickNoteWidgetDev`, 90-day validity) and writes a fresh random 24-character password to `%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\password.txt` with a user-only ACL. The password is **generated on first run and never printed, committed, or shared** — do not copy it out of that folder. Both the `dev-cert\` folder and `*.pfx` / `*.cer` are git-ignored.

Sign a built MSIX using the helper (reads `password.txt` at runtime):

```powershell
.\tools\Sign-DevMsix.ps1 <path-to-msix>
```

`make pack-signed` is reserved for **release** signing with a real code-signing cert and will refuse to run if `SIGNING_CERT` points inside any `dev-cert\` directory.

Install the accompanying `.cer` into **Trusted People** (LocalMachine) once, then install the MSIX:

```powershell
Add-AppxPackage <path-to-msix>
```

## Troubleshooting

- Logs (local only, no telemetry) — rolls at 1 MB:
  - Unpackaged: `%LocalAppData%\ObsidianQuickNoteWidget\log.txt`
  - Packaged: `%LocalAppData%\Packages\ObsidianQuickNoteWidget_<pfn>\LocalCache\Local\ObsidianQuickNoteWidget\log.txt`
- **After any manifest change that touches widget sizes or `<Definition>` elements, fully uninstall and reinstall** — Widget Host caches the definition list per-install and `-Force*` upgrades leave stale metadata. Pinned instances will be wiped.
- If the widget doesn't appear in the picker, also kill `Widgets.exe`, `WidgetService.exe`, `WebExperienceHost.exe`, `dasHost.exe` before reinstalling.
- If the card shows *"CLI not found"*, re-run Obsidian's **Register CLI** step and relaunch the Widget Board.

## Contributing

See [`.github/copilot-instructions.md`](.github/copilot-instructions.md) — it is the authoritative brief for maintainers and AI agents, covering architecture, the widget activation sequence, the verified Obsidian CLI surface, and every gotcha learned the hard way.

Quick reference:

- Build: `make build`
- Test: `make test`
- Package MSIX: `make pack`

## License

MIT — see [`LICENSE`](LICENSE).
