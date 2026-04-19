# Release Engineer — Pre-Release Readiness Report

**Target:** `C:\Users\lafia\csharp\obsidian_widget`
**Mode:** READ-ONLY pre-flight. No version bump, no tag, no pack, no publish performed.
**Verdict:** 🔴 **NO-GO**

---

## Executive summary

The project builds clean and all 132 tests pass in Release, but it is **not** in a releasable state. There is **no git repository** (so there is nothing to tag, no history to diff a version against, and no provenance), **no `CHANGELOG.md`**, **13 `dotnet format` violations**, a **dev-only signing identity** hardcoded into the manifest, **placeholder URLs and zero-SHA hashes** in the winget manifest, and **no `release.yml`** workflow to cut a release. Any of the top-three blockers below is sufficient to stop the release by itself; together they make a production release impossible today.

---

## Checks

### 1. Package version vs. git history — **🔴 BLOCKER (drift unverifiable)**
- `src\ObsidianQuickNoteWidget\Package.appxmanifest` → `Version="1.0.0.0"`
- `winget\ObsidianQuickNoteWidget.yaml` → `PackageVersion: 1.0.0`
- `winget\ObsidianQuickNoteWidget.installer.yaml` → `PackageVersion: 1.0.0`
- `winget\ObsidianQuickNoteWidget.locale.en-US.yaml` → `PackageVersion: 1.0.0`
- `Directory.Build.props` carries **no** `<Version>` / `<AssemblyVersion>` / `<FileVersion>` at all.
- Individual `.csproj` files carry no version element either.

Across the three winget files and the appxmanifest, the version strings are consistent at `1.0.0(.0)`. **However, there is no git repository at the root, at `src/`, or at any `src/*` subdirectory** (`git rev-parse` returns `fatal: not a git repository` everywhere). Therefore:
- There is no last tag to diff against.
- There is no commit history to confirm whether `1.0.0.0` was ever published or is a forever-placeholder.
- A release tag cannot be created at all.

**Severity: BLOCKER.**
**Fix:** `git init && git add -A && git commit -m "chore: initial import"` at the repo root, add a remote, push `main`. Until a repo exists, the release-engineer workflow (tag, annotated tag message from CHANGELOG, `git push origin vX.Y.Z`) cannot run.

### 2. `CHANGELOG.md` — **🔴 BLOCKER**
`Test-Path CHANGELOG.md` → **False**. No changelog exists at the root or anywhere in the tree.

**Severity: BLOCKER.** Per the release-engineer spec, *"Never bump a version without a matching CHANGELOG entry landing in the same commit."*
**Fix:** Create `CHANGELOG.md` at the root using Keep-a-Changelog format with an `## [Unreleased]` scaffold and an initial `## [1.0.0] - YYYY-MM-DD` section summarising the current feature set. Add link references at the bottom (`[Unreleased]`, `[1.0.0]`) pointing at the eventual GitHub compare URLs.

### 3. `dotnet build -c Release` — **✅ PASS**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```
All four projects (`Core`, `Core.Tests`, `Tray`, `Widget`) compiled. `Directory.Build.props` enforces `TreatWarningsAsErrors=true`, so a clean build implies no warnings anywhere.

### 4. `dotnet test -c Release --no-build` — **✅ PASS**
```
Passed!  - Failed: 0, Passed: 132, Skipped: 0, Total: 132  (net10.0, 123 ms)
```

### 5. `dotnet format --verify-no-changes` — **🟠 HIGH**
**13 whitespace violations**, all in:
- `src\ObsidianQuickNoteWidget.Core\Notes\NoteTemplates.cs` (lines 16, 17, 19, 20, 26, 28, 29)
- `tests\ObsidianQuickNoteWidget.Core.Tests\CardDataBuilderTests.cs` (lines 15, 64, 65, 141, 169)

All are `WHITESPACE` rules (extra spaces / line-break placement). No real code issues.

**Severity: HIGH** (spec §3 says "Fail closed on any red test, failed build, or failed lint").
**Fix:** `dotnet format ObsidianQuickNoteWidget.slnx` then commit as `style: apply dotnet format` in a dedicated commit **before** the release commit (the release commit must touch only version + CHANGELOG per spec §7).

### 6. Git tree clean? — **🔴 BLOCKER (no git tree at all)**
The directory is not under version control. There is no `.git` at the root, at `src/`, or at any `src/*` project. `git status`, `git log`, `git tag`, `git rev-parse --show-toplevel` all fail with `fatal: not a git repository`. This is the same finding as §1 but called out separately because it also blocks:
- Running `git log <last-tag>..HEAD` to populate CHANGELOG.
- Refusing to release from a dirty tree (no tree to check).
- Pushing `vX.Y.Z` tags.
- CI on `refs/tags/v*` (the `build.yml` tag trigger will never fire without a remote that has tags).

**Severity: BLOCKER.** **Fix:** same as §1 — initialise the repo, push to GitHub, configure the remote matching the `your-org/obsidian_widget` URL that already appears in the winget manifest (see §10).

### 7. MSIX packaging (`make pack`) — **🟠 HIGH (unsigned, dev identity)**
Not executed (per task constraints). Inspected `Makefile`:
```make
pack:
    $(DOTNET) publish $(WIDGET_PROJ) -c Release -p:Platform=$(PLATFORM) \
        -p:GenerateAppxPackageOnBuild=true \
        -p:AppxPackageSigningEnabled=false \
        -p:AppxBundle=Always \
        -p:UapAppxPackageBuildMode=SideloadOnly
```
- Produces a **sideload-only**, **unsigned** MSIX bundle.
- `pack-signed` target exists and is correct (consumes `$SIGNING_CERT` + `$SIGNING_PASSWORD`, sets `UapAppxPackageBuildMode=StoreUpload`) but is never invoked from CI.
- The target project is correctly configured (`<WindowsPackageType>MSIX</WindowsPackageType>`, `<EnableMsixTooling>true</EnableMsixTooling>`, x64/x86/arm64 platforms) — packaging should work locally.

**Severity: HIGH.** The `pack` target is fine for dev sideload but must not be used for a public release.
**Fix:** release builds must use `make pack-signed` with real secrets in CI (see §9). Also expose `ARCH=arm64` / x86 matrix in `pack` since the winget manifest advertises x64 **and** arm64 (§10) but the Makefile only builds one arch per invocation.

### 8. SemVer discipline — **🟠 HIGH**
The MSIX `Version="1.0.0.0"` uses the 4-part Windows form where the trailing `.0` is the *revision*, not semver patch. Windows ignores it for upgrade logic beyond ordering, so `1.0.0.0` vs `1.0.0.1` would both satisfy "same user-facing version 1.0.0" but upgrade in place. There is currently **no documented convention** for what the 4th segment means in this repo, and nothing ties the 3-part winget `PackageVersion` to the 4-part MSIX version.

**Severity: HIGH.**
**Fix:** document (in `README.md` or a new `RELEASING.md`) that (a) MSIX `Version` is always `X.Y.Z.0` with the 4th segment reserved for MSIX-only hotfix rebuilds that keep semver unchanged, (b) winget `PackageVersion` mirrors the first three components, (c) the annotated git tag is `vX.Y.Z`. Add a pre-release check (PowerShell one-liner or a `make verify-versions` target) that greps all four files and fails if they disagree.

### 9. Signing / real cert — **🔴 BLOCKER (dev publisher in manifest)**
`Package.appxmanifest` hardcodes:
```xml
<Identity Name="ObsidianQuickNoteWidget"
          Publisher="CN=ObsidianQuickNoteWidgetDev"
          Version="1.0.0.0" />
<Properties>
    ...
    <PublisherDisplayName>ObsidianQuickNoteWidget (dev)</PublisherDisplayName>
```
- `CN=ObsidianQuickNoteWidgetDev` is a self-signed dev identity. MSIX packages signed against it will only install on machines that have explicitly trusted the dev cert.
- The `(dev)` display name will be visible to users in Windows.
- `build.yml` packages with `AppxPackageSigningEnabled=false` — the artefact uploaded on tag is **unsigned** and cannot be installed without developer mode + manual trust.
- No `SIGNING_CERT` / `SIGNING_PASSWORD` secrets are configured in the workflow. `pack-signed` has no wiring in CI.

**Severity: BLOCKER** for a public release (anything outside private sideload).
**Fix:**
1. Acquire a code-signing certificate (Azure Trusted Signing, DigiCert, SSL.com, or a Store-submitted identity). The `Publisher` CN in `Package.appxmanifest` **must exactly match** the cert's subject or Windows will refuse to install.
2. Change `PublisherDisplayName` to the production display name (drop `(dev)`).
3. Add `SIGNING_CERT_BASE64` + `SIGNING_PASSWORD` repository secrets. In the release job, write the base64 back to a `.pfx`, call `make pack-signed`.
4. (Optional but recommended) migrate to Azure Trusted Signing so secrets are not PFX files at all.

### 10. Winget manifest ready to publish? — **🔴 BLOCKER**
`winget\ObsidianQuickNoteWidget.installer.yaml` contains **placeholders on every required field**:
- `InstallerUrl: https://github.com/your-org/obsidian_widget/releases/download/v1.0.0/...` — `your-org` is literal placeholder text.
- `InstallerSha256: 0000...` (x64) and `0000...` (arm64) — zero hashes.
- `SignatureSha256: 0000...` (x64 and arm64) — zero hashes; also impossible to populate without a real signing cert (§9).

`winget\ObsidianQuickNoteWidget.locale.en-US.yaml`:
- `Publisher: ObsidianQuickNoteWidget` — should be the legal publisher name matching the cert subject.
- `PublisherUrl` and `PublisherSupportUrl` point at `your-org/obsidian_widget`.

`PackageIdentifier: ObsidianQuickNoteWidget` is also not a valid Community Repo identifier — winget requires the `Publisher.PackageName` two-segment form (e.g. `YourOrg.ObsidianQuickNoteWidget`).

**Severity: BLOCKER.** Submitting this manifest as-is to `microsoft/winget-pkgs` would be rejected by the validation pipeline.
**Fix:**
1. Pick a real publisher namespace, rename `PackageIdentifier` to `Publisher.PackageName` form, rename the `winget/` directory structure to the expected `manifests/<letter>/<Publisher>/<PackageName>/<Version>/` layout if intending to submit upstream.
2. Replace `your-org` with the actual GitHub org in all three files and in the README under `winget/`.
3. Populate `InstallerSha256` / `SignatureSha256` from the actual signed MSIX (automate: compute hashes in the release workflow and `sed` them into the YAML before upload).
4. Validate with `winget validate --manifest winget/` before PR.

### 11. Release workflow — **🔴 BLOCKER (missing)**
- `.github/workflows/build.yml` exists and handles PR/main CI plus producing an unsigned MSIX on `v*.*.*` tags (good baseline).
- `.github/workflows/release.yml` — **does not exist**.

What `build.yml` is missing for an actual release:
- No `gh release create` step — tags produce no GitHub Release.
- No signing step (see §9).
- No changelog extraction (body of the GitHub Release should come from `CHANGELOG.md`).
- No winget manifest update / PR step.
- No `windows-latest` matrix across architectures — single x64 publish only.
- No smoke-test of the packaged MSIX (e.g. `Add-AppxPackage -Register` + launch the COM server) before publishing.

**Severity: BLOCKER.**
**Fix:** add `.github/workflows/release.yml` triggered on `push: tags: ['v*.*.*']` that:
1. Builds + tests (reuse the `build.yml` logic via a reusable workflow or duplicate).
2. Runs `dotnet format --verify-no-changes` as a hard gate.
3. Runs `make pack-signed` for each arch (x64 + arm64) using cert secrets.
4. Computes SHA-256 for each MSIX and for each `.appxsig`.
5. Creates the GitHub Release with the extracted `## [X.Y.Z]` slice of CHANGELOG as the body, attaches the signed MSIX bundle(s).
6. Optionally opens a PR against `microsoft/winget-pkgs` with the populated manifests (wingetcreate / komac).

---

## Ranked blockers

| # | Blocker | Severity | Blocks release? |
|---|---|---|---|
| 1 | Not a git repository — cannot tag, no history | 🔴 BLOCKER | Yes |
| 2 | No `CHANGELOG.md` | 🔴 BLOCKER | Yes |
| 3 | Manifest uses dev publisher `CN=ObsidianQuickNoteWidgetDev`; no real cert; CI builds unsigned | 🔴 BLOCKER | Yes |
| 4 | Winget manifest is placeholders (`your-org`, zero SHAs, wrong `PackageIdentifier` form) | 🔴 BLOCKER | Yes |
| 5 | No `release.yml` workflow | 🔴 BLOCKER | Yes |
| 6 | 13 `dotnet format` whitespace errors | 🟠 HIGH | Gate in release workflow must fail |
| 7 | `make pack` is unsigned sideload-only; no CI wiring for `pack-signed` | 🟠 HIGH | Release artifact would be unusable |
| 8 | SemVer convention for MSIX 4th segment undocumented; no version-consistency check | 🟠 HIGH | Drift risk |

**Passing checks:** `dotnet build -c Release` (0W/0E), `dotnet test` (132/132), version strings are internally consistent at `1.0.0(.0)`, MSIX project configuration is structurally correct, `pack-signed` Makefile target is well-formed.

---

## Verdict

🔴 **NO-GO.** Five independent blockers, any of which stops the release on its own. Recommended order of operations before the next attempt:

1. `git init`, push to the real org, replace `your-org` everywhere (§1, §6, §10).
2. `dotnet format` + commit (§5).
3. Author `CHANGELOG.md` with `[Unreleased]` + `[1.0.0]` (§2).
4. Acquire a real code-signing cert; update `Package.appxmanifest` `Publisher` + `PublisherDisplayName`; wire `SIGNING_CERT_BASE64` + `SIGNING_PASSWORD` secrets (§9).
5. Fix winget `PackageIdentifier` form; populate real URLs; automate SHA-256 substitution (§10).
6. Add `.github/workflows/release.yml` that signs, publishes a GitHub Release from the CHANGELOG slice, and attaches signed MSIX bundles for x64 + arm64 (§11).
7. Document the MSIX-4-part-vs-winget-3-part versioning convention + add `make verify-versions` (§8).

Re-run this check after those seven changes.
