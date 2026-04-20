# Contributing to noteshard

Thanks for your interest! This is a Windows 11-only Widget Board provider
for [Obsidian](https://obsidian.md) built on .NET 10 + Windows App SDK.
All contributions go through Pull Requests against `main`.

## Workflow

1. **Fork or branch** (no direct pushes to `main` — enforced by branch protection):
   ```powershell
   git checkout -b fix/short-description
   ```
2. **Build & test locally** before pushing:
   ```powershell
   dotnet build ObsidianQuickNoteWidget.slnx -c Release -p:Platform=x64
   dotnet test  ObsidianQuickNoteWidget.slnx -c Release
   ```
   CI uses the same commands. `TreatWarningsAsErrors=true` is on — Release
   builds must be 0W / 0E.
3. **Open a PR** using the template. CI runs the full Release suite + version
   triple-sync check and attaches an unsigned MSIX artifact you can sideload
   to smoke-test.
4. **Merge** once the `build-and-test` check is green. Linear history only —
   squash-merge from the GitHub UI.

## Releasing

Only `@SchwartzKamel` releases today. See
[`docs/contributing/release.md`](docs/contributing/release.md) for the full
procedure. Short version:

1. Bump the four version strings (manifest + 3 winget YAMLs) + add a
   `CHANGELOG.md` `[x.y.z.w]` section, all on a PR.
2. After merge, tag on `main`:
   ```powershell
   git tag v1.2.3.4
   git push origin v1.2.3.4
   ```
3. `release.yml` picks it up, signs the MSIX with the repo cert, and
   creates a GitHub Release with the MSIX + public `.cer` attached.

## Deeper docs

- [`docs/contributing/architecture.md`](docs/contributing/architecture.md) — seams, DI boundaries
- [`docs/contributing/testing.md`](docs/contributing/testing.md) — xUnit + BDD pattern
- [`docs/contributing/release.md`](docs/contributing/release.md) — full release runbook
- [`docs/contributing/security.md`](docs/contributing/security.md) — audit scope

## Ground rules

- Windows 11 only. No Linux / macOS / cross-plat assumptions.
- No secrets, PFX private keys, or personal `%UserProfile%` paths in the
  repo. See [`SECURITY.md`](SECURITY.md).
- Every behavioral change to `ObsidianWidgetProvider` needs a BDD scenario.
  The typed-text-wipe regression (1.0.0.9) is the cautionary tale.

## First-time CI setup (maintainer-only)

To enable signed releases on this repo:

```powershell
# 1. Bootstrap the signing cert (one time)
./scripts/bootstrap-signing-cert.ps1

# 2. Paste the two secrets it prints into the repo:
#      SIGNING_PFX_BASE64, SIGNING_PFX_PASSWORD

# 3. Commit the public .cer (DO NOT commit the .pfx or .b64)
git add scripts/signing/noteshard-signing.cer
git commit -m "chore(ci): add signing cert public half"

# 4. Apply branch protection (requires gh CLI logged in as admin)
./scripts/apply-branch-protection.ps1
```
