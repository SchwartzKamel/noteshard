# Winget manifest (skeleton)

These files describe the app for submission to
[`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs).

Fill in the `PackageVersion`, `InstallerUrl`, and `InstallerSha256` fields with
values from the signed MSIX release asset, then submit a pull request to the
winget-pkgs repository.

Files:
- `ObsidianQuickNoteWidget.installer.yaml` — installer metadata
- `ObsidianQuickNoteWidget.locale.en-US.yaml` — English locale
- `ObsidianQuickNoteWidget.yaml` — version manifest root

Users will then be able to install with:

```powershell
winget install ObsidianQuickNoteWidget
```
