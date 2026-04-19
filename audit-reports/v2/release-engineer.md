# Release Engineer — Pre-Release Readiness Report (v2)

**Target:** `C:\Users\lafia\csharp\obsidian_widget`
**Mode:** READ-ONLY re-sweep. No version bump, no tag, no pack, no publish performed.
**Prior verdict:** 🔴 NO-GO (5 blockers + 3 highs)
**New verdict:** 🟡 **CONDITIONAL GO** — dev-sideload release is ready; public winget/Store publish still blocked on a production signing cert + release.yml + populated SHA-256s.

---

## Executive summary

Six of the eight prior findings are resolved or materially reduced. The repo is now a real git tree with a clean working copy and a well-formed CHANGELOG. Dev-cert password handling is hardened (security F-01). Winget `PackageIdentifier` is in the valid two-segment form. `dotnet build -c Release`, `dotnet test`, and `dotnet format --verify-no-changes` are all green, with the test count having grown from 132 → **199**. A minor version-drift issue and one still-missing release workflow remain; neither blocks a dev/sideload cut, but both block a public publish.

---

## Blocker-by-blocker verification

### B1. Git repo exists — **✅ RESOLVED**
- `git rev-parse --is-inside-work-tree` → `true`.
- `git log --oneline`:
  - `d2cb0f6 (HEAD -> main) Security hardening: F-01 dev-cert pwd, F-02 PATH, F-03 log sanitization`
  - `f12a196 Initial commit: widget board provider + audit-driven fixes`
- `git status --short` → empty (clean tree).
- `git tag` → empty (no tags yet).
- `git remote -v` → empty (no remote wired yet).

**Residual:** no remote and no `v*` tag. Neither blocks a local release-engineer dry run, but the real cut must push to a remote and create an annotated `v0.1.0` (or `v1.0.0`) tag.

### B2. `CHANGELOG.md` — **✅ RESOLVED**
File present, Keep-a-Changelog format, SemVer linked. Current structure:
- `## [Unreleased]` with `### Fixed` (8 bullets mapping to audit fix IDs) and `### Security` (3 bullets mapping to F-01/F-02/F-03).
- `## [0.1.0] - initial scaffolding` summarising feature set.

**Residual (minor, not a blocker):** the section header for the released build is `[0.1.0]`, but `Package.appxmanifest` carries `Version="1.0.0.1"` and the winget manifests carry `PackageVersion: 1.0.0`. Either bump the CHANGELOG next-released section to `[1.0.0]`, or bump the manifests down to `0.1.0` — they must agree at tag time. This is the last piece of the versioning convention from §8 below.

### B3. Dev publisher / cert + password flow — **🟡 PARTIAL (dev OK, prod-cert still required)**
Two new tools in `tools\`:
- **`New-DevCert.ps1`** — generates a 24-char URL/shell-safe random password with `RandomNumberGenerator`, creates `CN=ObsidianQuickNoteWidgetDev` self-signed cert (RSA-2048, SHA-256, 90-day NotAfter), exports to `%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\{dev.pfx, dev.cer, password.txt}`, and stamps a user-only ACL on `password.txt` (owner + SYSTEM + Administrators only, inheritance disabled). Password is never echoed; `-Force` rotates. Directly resolves security-auditor F-01 (CWE-798).
- **`Sign-DevMsix.ps1`** — reads the password from the ACL-locked file at runtime, invokes `signtool sign /fd SHA256 /a /f ... /p $password` against the supplied MSIX, and clears `$password` afterwards.

**Residual blocker for public publish:** `src\ObsidianQuickNoteWidget\Package.appxmanifest` still hardcodes `Publisher="CN=ObsidianQuickNoteWidgetDev"`, `PublisherDisplayName="ObsidianQuickNoteWidget (dev)"`, `Version="1.0.0.1"`. That is correct for sideload smoke-testing but must be swapped to the real cert's subject + production display name before a Store/winget publish. No `SIGNING_CERT_BASE64`/`SIGNING_PASSWORD` secrets in CI yet.

### B4. Winget manifest — **🟡 PARTIAL (placeholders mostly replaced; SHAs + release.yml gating remain)**
`winget\ObsidianQuickNoteWidget{.yaml,.installer.yaml,.locale.en-US.yaml}` now:
- `PackageIdentifier: Lafiamafia.ObsidianQuickNoteWidget` — valid two-segment community-repo form (fixed from the bare `ObsidianQuickNoteWidget`).
- `PublisherUrl` / `PublisherSupportUrl` / `PackageUrl` / `InstallerUrl` all point at `github.com/lafiamafia/ObsidianQuickNoteWidget` — no remaining `your-org` literals.
- `ManifestVersion: 1.6.0`, `InstallerType: msix`, x64 + arm64 installers declared, locale block carries `Moniker`, `Tags`, `License: MIT`, and a real description.

**Outstanding TODOs (documented as comments at the top of each file and annotated per-field):**
1. `InstallerSha256` (x64 + arm64): still `00000…` — must be regenerated from the real signed MSIX.
2. `SignatureSha256` (x64 + arm64): still `00000…` — ditto, and intrinsically blocked by B3.
3. Three `# TODO:` banner comments noting the repo URL is placeholder-until-pushed and that the 3-part `PackageVersion` has a 4-part `Package.appxmanifest` counterpart (`1.0.0.1`).
4. `Publisher: ObsidianQuickNoteWidget` should eventually match the legal CN on the production signing cert.
5. If submitting to `microsoft/winget-pkgs`, files must be relocated into `manifests/l/Lafiamafia/ObsidianQuickNoteWidget/1.0.0/`.

None of these block a local/private release; all four block a public winget PR.

### B5. `.github/workflows/release.yml` — **❌ STILL MISSING (deferred per task)**
- `.github/workflows/build.yml` is the only workflow file.
- No `release.yml`; no `gh release create`, no tag-gated signed-MSIX matrix, no CHANGELOG-slice release body, no wingetcreate/komac step.

Per task instructions this is acknowledged and deferred.

---

## Gate results (Release)

| Gate | Command | Result |
|---|---|---|
| Build | `dotnet build -c Release` | **✅ 0W / 0E**, all 4 projects linked |
| Test  | `dotnet test -c Release --no-build` | **✅ Passed: 199, Failed: 0, Skipped: 0** (was 132/132 in v1) |
| Format | `dotnet format ObsidianQuickNoteWidget.slnx --verify-no-changes` | **✅ ExitCode=0** (was 13 whitespace errors in v1) |
| Git tree | `git status --short` | **✅ clean** |

The earlier v1 HIGH findings — 13 `dotnet format` violations (§5) and tree-cleanliness (§6) — are fully resolved.

---

## Residual (non-blocker) items from v1

- **§7 `make pack` is unsigned sideload-only.** Unchanged — `pack-signed` target still exists, still has no CI wiring. Not re-exercised.
- **§8 SemVer / 4-part convention.** The three winget files now carry explanatory TODO banners noting "PackageVersion uses the 3-part winget convention; the 4-part MSIX counterpart is 1.0.0.1 (see Package.appxmanifest Identity/@Version)." That covers the convention, but an automated `make verify-versions` guard and a CHANGELOG header aligned with the manifest version are still open (see B2 Residual).

---

## Ranked outstanding items

| # | Item | Severity | Blocks dev cut? | Blocks public publish? |
|---|---|---|---|---|
| 1 | Production code-signing cert not acquired; manifest still `CN=ObsidianQuickNoteWidgetDev` + `(dev)` display name; no CI secrets | 🔴 BLOCKER | No | **Yes** |
| 2 | `release.yml` not authored | 🔴 BLOCKER | No | **Yes** |
| 3 | Winget `InstallerSha256` / `SignatureSha256` zero-filled (downstream of #1) | 🔴 BLOCKER | No | **Yes** |
| 4 | No git remote + no `v*.*.*` tag yet | 🟠 HIGH | Borderline | **Yes** |
| 5 | CHANGELOG released-section header (`[0.1.0]`) disagrees with manifest version (`1.0.0.1`) / winget (`1.0.0`) | 🟠 HIGH | **Yes** (would stop release-engineer at step 6) | Yes |
| 6 | No `make verify-versions` guard / version-drift linter | 🟡 MEDIUM | No | No |
| 7 | `make pack-signed` still not invoked from CI | 🟡 MEDIUM | No | Yes (downstream of #2) |

---

## Verdict

🟡 **CONDITIONAL GO.**

- **Dev/sideload cut:** ready pending only the CHANGELOG-vs-manifest version alignment (item #5). Build + tests + format + clean tree + working signing flow all green.
- **Public (winget / Store) publish:** still blocked on (a) a production code-signing cert and updated `Publisher`/`PublisherDisplayName`, (b) a `.github/workflows/release.yml` that signs + computes SHAs + creates the GitHub Release, (c) regeneration of the four zero-valued SHA-256 fields from the real signed MSIX, and (d) a real GitHub remote + pushed annotated `vX.Y.Z` tag.

Recommended order before the next sweep:
1. Decide target version (`0.1.0` vs `1.0.0`) and align CHANGELOG header + `Package.appxmanifest` `Version` + winget `PackageVersion` (item #5).
2. Add a remote, push `main`, and dry-run an annotated tag locally (item #4).
3. Acquire signing cert, wire CI secrets, update manifest publisher/display (items #1, #7).
4. Author `release.yml` (item #2) that signs, computes SHA-256s, substitutes them into the winget YAMLs, and creates the GitHub Release from the CHANGELOG slice (item #3).
5. Add `make verify-versions` as a fail-closed gate (item #6).
