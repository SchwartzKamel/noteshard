# Release Engineer — Pre-Release Readiness Report (v3)

**Target:** `C:\Users\lafia\csharp\obsidian_widget`
**Mode:** READ-ONLY re-sweep. No version bump, tag, pack, or publish performed.
**Prior verdicts:** v1 🔴 NO-GO (5 blockers + 3 highs) · v2 🟡 CONDITIONAL GO (3 public-publish blockers)
**New verdict:** 🟡 **CONDITIONAL GO (regressed on version hygiene)** — dev/sideload cut is still viable but a new drift has opened between the manifest (1.0.0.2) and both the winget manifests (still 1.0.0 / TODO references 1.0.0.1) and the CHANGELOG (no `[Unreleased]` entry for the dropdown revert). Public publish still blocked on the same three v2 items.

---

## Executive summary

All three v2 hard gates remain green: `dotnet build -c Release` 0W/0E, `dotnet test` 199/199, `dotnet format --verify-no-changes` ExitCode 0, and the working tree is clean on `main` with three commits. However the v2 "HIGH" version-drift item has regressed into a fresh inconsistency: `Package.appxmanifest` moved from `1.0.0.1` → `1.0.0.2`, but the three winget manifest files were **not** bumped in lockstep (they still read `PackageVersion: 1.0.0` and their TODO banner comments still say "4-part MSIX counterpart is 1.0.0.1"). In addition, the most recent commit (`750f032 Restore compact folder dropdown + add separate 'new folder' text input`) lacks a `## [Unreleased]` CHANGELOG entry and directly contradicts the existing v2 `[Unreleased]` bullet ("Folder ChoiceSet switched from `compact` to `expanded`…") — the changelog now describes a change that was reverted and not the change that landed.

The three public-publish blockers from v2 are unchanged:
1. Production code-signing cert absent — `Publisher="CN=ObsidianQuickNoteWidgetDev"` and `(dev)` display name still in the manifest.
2. `.github/workflows/release.yml` still missing; `build.yml` is the only workflow.
3. Four zero-filled `InstallerSha256` / `SignatureSha256` fields in the winget installer manifest (downstream of #1).

---

## Verified state

### Git tree
```
750f032 (HEAD -> main) Restore compact folder dropdown + add separate 'new folder' text input
d2cb0f6 Security hardening: F-01 dev-cert pwd, F-02 PATH, F-03 log sanitization
f12a196 Initial commit: widget board provider + audit-driven fixes
```
- `git status --short` → empty (clean).
- `git tag` → empty.
- `git remote -v` → empty.
- Three commits on `main`, matches the task brief.

### Gate results (Release)

| Gate | Command | Result |
|---|---|---|
| Build | `dotnet build -c Release` | ✅ **0 Warning(s), 0 Error(s)** (4 projects linked) |
| Test  | `dotnet test -c Release --no-build` | ✅ **Passed: 199, Failed: 0, Skipped: 0, Total: 199** (412 ms) |
| Format | `dotnet format ObsidianQuickNoteWidget.slnx --verify-no-changes --no-restore` | ✅ **ExitCode 0** |
| Tree | `git status --short` | ✅ clean |

No regression in the three hard gates since v2 (v1: 13 format errors, 132 tests → v2: 0/199 → v3: 0/199).

### Version-string sweep

| File | Field | Value |
|---|---|---|
| `src\ObsidianQuickNoteWidget\Package.appxmanifest` | `Identity/@Version` | **`1.0.0.2`** (bumped from 1.0.0.1 since v2) |
| `src\ObsidianQuickNoteWidget\Package.appxmanifest` | `Identity/@Publisher` | `CN=ObsidianQuickNoteWidgetDev` *(unchanged, dev)* |
| `src\ObsidianQuickNoteWidget\Package.appxmanifest` | `PublisherDisplayName` | `ObsidianQuickNoteWidget (dev)` *(unchanged)* |
| `winget\ObsidianQuickNoteWidget.yaml` | `PackageVersion` | `1.0.0` |
| `winget\ObsidianQuickNoteWidget.installer.yaml` | `PackageVersion` | `1.0.0` |
| `winget\ObsidianQuickNoteWidget.locale.en-US.yaml` | `PackageVersion` | `1.0.0` |
| `winget\*.yaml` | TODO banner | "…4-part MSIX counterpart is **1.0.0.1**…" *(stale — should be 1.0.0.2 or dropped)* |
| `winget\ObsidianQuickNoteWidget.installer.yaml` | `InstallerSha256` (x64, arm64) | `0000…` (both) |
| `winget\ObsidianQuickNoteWidget.installer.yaml` | `SignatureSha256` (x64, arm64) | `0000…` (both) |
| `CHANGELOG.md` | Released header | `[0.1.0] - initial scaffolding` |
| `CHANGELOG.md` | `[Unreleased]` | v2 bullets only; no entry for commit `750f032` |

The v2 residual ("CHANGELOG header `[0.1.0]` disagrees with manifest `1.0.0.1`") has **widened** — the manifest is now `1.0.0.2`, the winget files still say `1.0.0`, and the CHANGELOG still only mentions `0.1.0` released + the `[Unreleased]` set from v2 without the latest commit.

---

## Blocker re-verification

### B1. Production code-signing cert — ❌ **STILL ABSENT**
`src\ObsidianQuickNoteWidget\Package.appxmanifest` lines 13–14:
```xml
Publisher="CN=ObsidianQuickNoteWidgetDev"
Version="1.0.0.2" />
```
`PublisherDisplayName="ObsidianQuickNoteWidget (dev)"`. No `SIGNING_CERT_BASE64` / `SIGNING_PASSWORD` secrets wired in CI. `tools\New-DevCert.ps1` + `tools\Sign-DevMsix.ps1` remain the only signing path — correct for sideload, insufficient for winget/Store. **Unchanged since v2.**

### B2. `.github/workflows/release.yml` — ❌ **STILL MISSING**
`.github\workflows\` contains only `build.yml`. No tag-triggered signed-MSIX matrix, no `gh release create`, no CHANGELOG-slice body, no SHA-256 substitution into winget YAML, no wingetcreate/komac step. **Unchanged since v2.**

### B3. Winget `*Sha256` fields — ❌ **STILL ZERO-FILLED**
Four occurrences (x64 + arm64 × Installer + Signature) all `0000…0000`. Downstream of B1. **Unchanged since v2.**

### B4 (NEW). Manifest ↔ winget version drift — 🟠 **REGRESSED**
`Package.appxmanifest` was bumped 1.0.0.1 → 1.0.0.2 without a lockstep update of:
- `winget\ObsidianQuickNoteWidget.yaml` → still `PackageVersion: 1.0.0`
- `winget\ObsidianQuickNoteWidget.installer.yaml` → still `PackageVersion: 1.0.0`
- `winget\ObsidianQuickNoteWidget.locale.en-US.yaml` → still `PackageVersion: 1.0.0`
- The TODO banner in all three winget files still references `1.0.0.1` as the "4-part MSIX counterpart".

Either (a) 1.0.0 is the user-facing winget version and the 4th segment is an MSIX-only hotfix counter (consistent with the v2 stated convention, in which case the **banner** needs updating from 1.0.0.1 → 1.0.0.2 but `PackageVersion` can stay 1.0.0), or (b) the 4th segment matters and winget should move to 1.0.0.2. The documented convention favors (a) — so this is at minimum a stale-comment drift, and there is still no automated `make verify-versions` guard catching it. Per release-engineer spec step 5 ("Bump in lockstep"), this must be resolved before a tag.

### B5 (NEW). CHANGELOG missing entry for dropdown revert — 🟠 **HIGH**
Commit `750f032` ("Restore compact folder dropdown + add separate 'new folder' text input") landed after v2 and is not represented in `CHANGELOG.md`. Worse, the existing `[Unreleased] → Fixed` block from v2 contains:
> Folder ChoiceSet switched from `compact` to `expanded` on medium+large so "type new or pick" works.
That bullet is now **false** — the new commit restored the compact dropdown and added a separate text input instead. The CHANGELOG describes a change that no longer exists in HEAD.

Release-engineer spec §6: "Do not invent changelog content. Every bullet must map to a real commit or PR since the last tag." Inverse also holds — every commit must map to a bullet, or the next release notes are wrong. A release cut today would ship a misleading changelog slice.

---

## Ranked outstanding items

| # | Item | Severity | Blocks dev cut? | Blocks public publish? | Δ vs v2 |
|---|---|---|---|---|---|
| 1 | Production code-signing cert not acquired; manifest still `CN=ObsidianQuickNoteWidgetDev` + `(dev)`; no CI secrets | 🔴 BLOCKER | No | **Yes** | unchanged |
| 2 | `release.yml` not authored | 🔴 BLOCKER | No | **Yes** | unchanged |
| 3 | Winget `InstallerSha256` / `SignatureSha256` zero-filled (downstream of #1) | 🔴 BLOCKER | No | **Yes** | unchanged |
| 4 | CHANGELOG `[Unreleased]` missing entry for `750f032` dropdown revert **and** contains a now-false "compact→expanded" bullet that must be removed or replaced | 🟠 HIGH | **Yes** (release-engineer step 6 would ship wrong notes) | **Yes** | **NEW** |
| 5 | Manifest `1.0.0.2` vs winget `1.0.0` vs CHANGELOG released-section `[0.1.0]` — three-way version mismatch; winget TODO banners still reference stale `1.0.0.1` | 🟠 HIGH | **Yes** (step 5 "bump in lockstep") | Yes | **widened** from v2 |
| 6 | No git remote + no `v*.*.*` tag yet | 🟠 HIGH | Borderline | **Yes** | unchanged |
| 7 | No `make verify-versions` guard / version-drift linter (would have caught #5) | 🟡 MEDIUM | No | No | unchanged |
| 8 | `make pack-signed` still not invoked from CI | 🟡 MEDIUM | No | Yes (downstream of #2) | unchanged |

**Passing checks:** build, test (199/199), format, clean tree, valid winget `PackageIdentifier` form, dev-cert password flow, MSIX project configuration.

---

## Verdict

🟡 **CONDITIONAL GO — but one step worse than v2 for a dev cut.**

- **Dev/sideload cut:** was "ready pending only CHANGELOG-vs-manifest alignment" in v2. Now has *two* must-fix items before release-engineer can proceed without tripping its own guardrails — the new #4 (missing + contradicted CHANGELOG entry) and the widened #5 (three-way version mismatch). Both are cheap to fix (one edit each to `CHANGELOG.md` and to the winget TODO banner, plus deciding whether to touch `PackageVersion`).
- **Public (winget / Store) publish:** still blocked on the same three v2 items (production signing cert, `release.yml`, real SHA-256s) — none resolved, none regressed.

---

## Top-3 to fix next (ordered)

1. **Reconcile CHANGELOG `[Unreleased]` with HEAD.** Remove the now-false "Folder ChoiceSet switched from `compact` to `expanded`…" bullet; add a new `### Changed` entry for commit `750f032` ("Restore compact folder dropdown; add separate text input for new-folder entry"). Every `## [Unreleased]` bullet must map 1:1 to a commit in `f12a196..HEAD`, and every such commit must appear.
2. **Resolve the 1.0.0 / 1.0.0.2 drift.** Confirm the documented convention (winget 3-part mirrors MSIX first-three; 4th segment is MSIX-only hotfix counter) and update the three winget TODO banners from "1.0.0.1" → "1.0.0.2". If instead the 4th bump is semantically meaningful, bump winget `PackageVersion` to match and regenerate the manifest folder layout. Either way, add a `make verify-versions` gate (grep all four files, diff the first three segments, fail non-zero) so this can't drift silently again — this single step retires items #5 and #7.
3. **Author `.github\workflows\release.yml`.** Trigger on `push: tags: ['v*.*.*']`; build + test + `dotnet format --verify-no-changes` as hard gates; `make pack-signed` with `SIGNING_CERT_BASE64` / `SIGNING_PASSWORD` secrets once the production cert lands (item #1); compute SHA-256 for each MSIX + `.appxsig`; `sed` the four zero hashes into `winget\*.installer.yaml`; `gh release create vX.Y.Z --notes-file <changelog-slice>` attaching the signed x64 + arm64 bundles. Landing this workflow retires items #2, #3, and #8 in one pass and is the single highest-leverage remaining piece of work for a public publish.
