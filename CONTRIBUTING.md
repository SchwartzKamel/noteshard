# Contributing to noteshard

This is a Windows 11-only Widget Board provider for
[Obsidian](https://obsidian.md), built on .NET 10 + Windows App SDK.

The maintainer (`@SchwartzKamel`) pushes direct to `main` — console-cowboy
mode. Branch protection is intentionally off. Outside contributors are
welcome to open PRs though; the PR template and CI will guide you.

## Local dev loop

```powershell
dotnet build ObsidianQuickNoteWidget.slnx -c Release
dotnet test  ObsidianQuickNoteWidget.slnx -c Release
```

`TreatWarningsAsErrors=true` — Release builds must be 0W / 0E. CI runs the
same commands on every push to `main` and every PR.

## Releasing

Tag-triggered. Bump the four version strings (manifest + 3 winget YAMLs),
add a `CHANGELOG.md` `[x.y.z.w]` section, commit, then:

```powershell
git tag v1.2.3.4
git push origin v1.2.3.4
```

`release.yml` runs verify-versions → build → test → publish MSIX → sign
(if `SIGNING_PFX_BASE64`/`SIGNING_PFX_PASSWORD` secrets are set) → creates
a GitHub Release. Missing secrets fall back to an unsigned MSIX attached
to the release.

See [`docs/contributing/release.md`](docs/contributing/release.md) for the
full runbook.

## Deeper docs

- [`docs/contributing/architecture.md`](docs/contributing/architecture.md) — seams, DI boundaries
- [`docs/contributing/testing.md`](docs/contributing/testing.md) — xUnit + BDD pattern
- [`docs/contributing/release.md`](docs/contributing/release.md) — full release runbook
- [`docs/contributing/security.md`](docs/contributing/security.md) — audit scope

## Ground rules

- Windows 11 only. No Linux / macOS / cross-plat assumptions.
- No secrets, PFX private keys, or personal `%UserProfile%` paths in the repo.
  See [`SECURITY.md`](SECURITY.md).
- Every behavioral change to `ObsidianWidgetProvider` needs a BDD scenario.
  The typed-text-wipe regression (1.0.0.9) is the cautionary tale.

## First-time CI signing setup (maintainer-only, optional)

Releases work unsigned out of the box. To flip on signed releases:

```powershell
# 1. Bootstrap the signing cert (one time)
./scripts/bootstrap-signing-cert.ps1

# 2. Paste the two secrets it prints:
gh secret set SIGNING_PFX_BASE64 < signing-pfx.b64
gh secret set SIGNING_PFX_PASSWORD   # paste password

# 3. Commit the public .cer (NEVER the .pfx or .b64)
git add scripts/signing/noteshard-signing.cer
git commit -m "chore(ci): add signing cert public half"
Remove-Item signing-pfx.b64
```
