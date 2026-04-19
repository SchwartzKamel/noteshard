# Security Policy

> **Audience:** security researchers, downstream users, contributors.

## Supported versions

Only the latest published release on `main` is supported. Active version: see [`CHANGELOG.md`](CHANGELOG.md).

## Reporting a vulnerability

**Please do not open public GitHub issues for security reports.**

Instead, report privately via GitHub's [Security Advisories](../../security/advisories/new) form on this repository. If that's not available to you, email the maintainer at the address on the GitHub profile of the repo owner.

Include, if known:

- Affected version (e.g. `1.0.0.7`)
- Component (COM widget, tray app, Core library, packaging, CLI wrapper)
- Reproduction steps or a minimal proof-of-concept
- Impact assessment (what an attacker can do)
- Suggested fix if you have one

I aim to acknowledge reports within **7 days** and ship a fix or coordinated disclosure within **30 days** for HIGH/CRITICAL issues, longer for lower-severity.

## Scope

In scope:

- The MSIX-packaged widget provider (`ObsidianQuickNoteWidget.exe`).
- The WinForms tray companion (`ObsidianQuickNoteTray.exe`).
- The shared `ObsidianQuickNoteWidget.Core` library.
- The dev-cert bootstrap scripts under `tools/`.
- Packaging and signing pipeline (`Package.appxmanifest`, `winget/*.yaml`, `tools/Sign-DevMsix.ps1`).

Out of scope:

- Vulnerabilities in Obsidian itself or its CLI — report those to the Obsidian project.
- Vulnerabilities in the Windows App SDK or Widget Host — report those to Microsoft.
- Attacks requiring physical access, malware already running as the user, or admin rights already granted.
- Social-engineering against the user that don't exploit a coding defect.

## Known considerations

- **Obsidian CLI trust boundary.** This widget shells out to the `obsidian` CLI with `ProcessStartInfo.ArgumentList` (no shell interpreter), so command arguments cannot be injected by user input. The CLI is resolved via a fixed search order (override env var → Program Files → LocalAppData → registry → PATH) to avoid PATH-prepend attacks.
- **Plugin Runner.** Actions added to the Plugin Runner widget execute arbitrary Obsidian commands by ID. Users are responsible for the command IDs they pin — treat the catalog like any other shortcut launcher.
- **Dev cert.** `tools/New-DevCert.ps1` writes the PFX and its random 24-char password to `%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\` with a user-only ACL. Neither file is committed; both are in `.gitignore`.
- **Logging.** User-controllable strings are sanitized (`FileLog.SanitizeForLogLine`) before reaching the log file to prevent log forgery.

## What I'd love reports about

- Anything that bypasses folder-path validation and lets a note land outside the intended vault folder tree.
- Anything that causes the widget provider to crash out of `OnActionInvoked` (Widget Host should never see an exception escape).
- Anything that lets a malicious `obsidian://` URI or adaptive-card payload trigger unexpected local execution.
- Supply-chain concerns with the MSIX / winget publishing pipeline.

Thank you for helping keep this project safe.
