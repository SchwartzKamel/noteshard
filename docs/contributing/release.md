# Release

> This page is for contributors cutting a release. The canonical path is
> **tag-triggered CI** — push a `vX.Y.Z.W` tag and
> [`../../.github/workflows/release.yml`](../../.github/workflows/release.yml)
> builds, tests, signs, and publishes the GitHub Release. The local dev-cert
> sideload flow further down is retained for pre-flight MSIX testing only.

Up: [`../README.md`](../README.md) (docs index)

Current shipped version: **1.0.0.10** — signed MSIX at
[latest release](https://github.com/SchwartzKamel/noteshard/releases/latest).

## Release checklist (cowboy edition)

Console-cowboy mode: the maintainer pushes direct to `main`. No PR, no branch
protection. Outside contributors are welcome to open PRs but that's not the
release path. Steps, top to bottom:

### 1. Local pre-flight

```powershell
dotnet build ObsidianQuickNoteWidget.slnx -c Release
dotnet test  ObsidianQuickNoteWidget.slnx -c Release
```

Expected: **0 Warning(s), 0 Error(s)** and **403/403 passed** (at
1.0.0.10 — see [`testing.md`](./testing.md) for the breakdown).
`TreatWarningsAsErrors=true` is global; if something warns, fix the code,
not the gate. CI runs the same commands in `ci.yml` on every push/PR.

### 2. Bump the four version strings

They must all agree on the 4-part `Major.Minor.Build.Revision`:

| File | Field |
| --- | --- |
| [`../../src/ObsidianQuickNoteWidget/Package.appxmanifest`](../../src/ObsidianQuickNoteWidget/Package.appxmanifest) | `<Identity Version="X.Y.Z.W" />` |
| [`../../winget/ObsidianQuickNoteWidget.yaml`](../../winget/ObsidianQuickNoteWidget.yaml) | `PackageVersion: X.Y.Z.W` |
| [`../../winget/ObsidianQuickNoteWidget.installer.yaml`](../../winget/ObsidianQuickNoteWidget.installer.yaml) | `PackageVersion: X.Y.Z.W` |
| [`../../winget/ObsidianQuickNoteWidget.locale.en-US.yaml`](../../winget/ObsidianQuickNoteWidget.locale.en-US.yaml) | `PackageVersion: X.Y.Z.W` |

[`../../scripts/verify-versions.ps1`](../../scripts/verify-versions.ps1) gates
CI — the `release.yml` workflow hard-fails if the tag doesn't match all four
strings. Run it locally to double-check:

```powershell
./scripts/verify-versions.ps1 -ExpectedVersion 1.0.0.10
```

Or quick grep:

```powershell
rg -n 'PackageVersion|Identity.*Version' winget src/ObsidianQuickNoteWidget/Package.appxmanifest
```

### 3. CHANGELOG entry

[`../../CHANGELOG.md`](../../CHANGELOG.md) follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/):

1. Add a `## [X.Y.Z.W] - YYYY-MM-DD` section.
2. Promote any `## [Unreleased]` bullets into it, grouped under
   `### Added` / `### Changed` / `### Fixed` / `### Security` / `### Removed`.
3. Leave an empty `## [Unreleased]` behind.
4. Every substantive commit since the previous tag must be represented, and
   every bullet must map to a real commit. Collapse or split as reads cleanly.

`scripts/extract-changelog.ps1` slices the `[X.Y.Z.W]` section out and feeds
it to the GitHub Release body — so the format has to parse.

### 4. Commit + push to main

```powershell
git add -A
git commit -m "release: X.Y.Z.W (short summary)"
git push origin main
```

Conventional-commits-ish shorthand for non-release commits: `fix:`, `feat:`,
`docs:`, `test:`, `refactor:`, `chore:`. Co-authored-by trailers for
agent-assisted commits.

### 5. Tag + push the tag

```powershell
git tag -a vX.Y.Z.W -m "noteshard X.Y.Z.W"
git push origin vX.Y.Z.W
```

### 6. CI takes over

[`../../.github/workflows/release.yml`](../../.github/workflows/release.yml)
fires on `v*.*.*.*` tag push and runs, in order:

1. `verify-versions.ps1` — tag must match all four version strings.
2. `dotnet restore` / `build -c Release` / `test -c Release` on the full slnx.
3. `dotnet publish` the widget csproj → MSIX (x64, `AppxBundle=Never`,
   `SideloadOnly`).
4. **Check `SIGNING_PFX_BASE64` secret.** If present, run
   [`../../scripts/sign-msix.ps1`](../../scripts/sign-msix.ps1) using the
   stable repo cert. If missing, emit a warning and ship **unsigned** — the
   asset filename carries a `-unsigned` suffix so it's obvious.
5. Stage assets: `ObsidianQuickNoteWidget_X.Y.Z.W_x64[-unsigned].msix` and
   (when signed) `noteshard-signing.cer` alongside, so users can import the
   public half into Trusted People and sideload.
6. Extract the matching CHANGELOG section via `extract-changelog.ps1`.
7. `softprops/action-gh-release@v2` publishes the GitHub Release with the
   MSIX + cert attached.

### 7. Verify

Check [https://github.com/SchwartzKamel/noteshard/releases](https://github.com/SchwartzKamel/noteshard/releases)
— the new release should show up within ~3–5 minutes of the tag push, with
the signed MSIX and `.cer` attached.

If something blows up, check the Actions tab. Common failure:
`verify-versions.ps1` finding drift — one of the four files didn't get
bumped. Fix, push, re-tag (`git tag -d vX.Y.Z.W && git push origin :vX.Y.Z.W`
then start over at step 5).

## Signing: who holds what

- **Public half** — `scripts/signing/noteshard-signing.cer` is committed.
  Users import it into **Trusted People (LocalMachine)** once to trust
  sideloaded MSIXes.
- **Private half** — base64-encoded PFX lives in the `SIGNING_PFX_BASE64`
  GitHub Actions secret; its password in `SIGNING_PFX_PASSWORD`. The PFX is
  never in the working tree.
- **Bootstrap** — [`../../scripts/bootstrap-signing-cert.ps1`](../../scripts/bootstrap-signing-cert.ps1)
  is the one-time maintainer script that generates the self-signed cert, drops
  the public `.cer` in `scripts/signing/`, and prints the base64 + password
  ready to paste into `gh secret set`. See
  [`../../CONTRIBUTING.md`](../../CONTRIBUTING.md#first-time-ci-signing-setup-maintainer-only-optional)
  for the exact invocation.
- **Secrets missing?** Release still ships — just unsigned (`-unsigned`
  filename suffix). Users have to enable Developer Mode and
  `Add-AppxPackage -AllowUnsigned` (or trust the cert once for signed
  builds). Flipping the secrets on is a maintainer decision, not a blocker.

---

## Local dev-cert sideload (optional pre-flight)

Everything below is for **testing an MSIX locally** before pushing a tag —
or running without the CI signing path at all. It is NOT the canonical
release flow.

### Build the MSIX

```powershell
dotnet publish src/ObsidianQuickNoteWidget/ObsidianQuickNoteWidget.csproj `
  -c Release -p:Platform=x64 `
  -p:GenerateAppxPackageOnBuild=true `
  -p:AppxPackageSigningEnabled=false `
  -p:AppxBundle=Always `
  -p:UapAppxPackageBuildMode=SideloadOnly
```

Output lands under
`src/ObsidianQuickNoteWidget/bin/x64/Release/net10.0-windows10.0.26100.0/AppPackages/…`.
`AppxPackageSigningEnabled=false` is deliberate — we sign as a separate step
so the password stays out of MSBuild's log.

`make pack` wraps the same command.

### Sign the MSIX (dev cert)

```powershell
.\tools\Sign-DevMsix.ps1 <path-to-msixbundle>
```

The script reads the password from
`%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\password.txt` (generated by
`tools\New-DevCert.ps1`, user-only ACL, 24 random chars — see
[`development.md`](./development.md#dev-cert-bootstrap) and
[`security.md`](./security.md) F-01). The underlying call is:

```
signtool sign /fd SHA256 /a /f <dev.pfx> /p <from-password.txt> <msix>
```

The password is never echoed and is cleared in the script's `finally`.
Rotate with `.\tools\New-DevCert.ps1 -Force`. Key rotation reuses the same
subject name so the import into **Trusted People (LocalMachine)** stays valid.

`make pack-signed` is reserved for real release signing (e.g. DigiCert /
SSL.com code-signing cert). It **refuses** to run when `SIGNING_CERT`
resolves to any `dev-cert\` path — this is the F-01 defence-in-depth in the
[`../../Makefile`](../../Makefile).

### Install test

```powershell
Add-AppxPackage -Path <path-to-msixbundle> -ForceApplicationShutdown
```

After a `<Definition>` change, do the full uninstall + reinstall — Widget
Host caches per-install metadata:

```powershell
Get-AppxPackage *ObsidianQuickNoteWidget* | Remove-AppxPackage
Add-AppxPackage <path>
```

### Kick Widget Host

```powershell
Get-Process Widgets,WidgetService -ErrorAction SilentlyContinue | Stop-Process -Force
```

In stubborn cases: also `WebExperienceHost.exe`, `dasHost.exe`, any lingering
`ObsidianQuickNoteWidget.exe`.

### Smoke test

1. Tail `%LocalAppData%\Packages\<pfn>\LocalCache\Local\ObsidianQuickNoteWidget\log.txt`.
2. ⊞+W → *+ Add widgets* → pin Obsidian Quick Note (small), Recent Notes, Plugin Runner.
3. Each should render within ~1–2 s. The log should show a matching
   `CreateWidget id=… kind=<definitionId> size=…` per pin.
4. Create a note from the small card; confirm log shows `verb=createNote`
   followed by a successful `Created:` parse.
5. For `Recent Notes`: click an entry; confirm it opens in Obsidian (and
   launches it if it was closed — 1.0.0.7).

## See also

- [`../../CONTRIBUTING.md`](../../CONTRIBUTING.md) — cowboy-mode contribution
  style + signing-secret bootstrap.
- [`development.md`](./development.md) — dev-cert setup + local install loop.
- [`security.md`](./security.md) — F-01 password-rotation scheme, F-15
  signtool `/p` residual.
- [`../../CHANGELOG.md`](../../CHANGELOG.md) — release history.

Up: [`../README.md`](../README.md)
