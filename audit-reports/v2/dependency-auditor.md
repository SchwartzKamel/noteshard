# Dependency Audit v2 — ObsidianQuickNoteWidget

- **Mode:** READ-ONLY verification sweep. No manifests or lockfiles mutated.
- **Scope:** all `.csproj` files in repo — 4 in the `.slnx` (`ObsidianQuickNoteTray`, `ObsidianQuickNoteWidget.Core`, `ObsidianQuickNoteWidget`, `ObsidianQuickNoteWidget.Core.Tests`) plus `tools/AppExtProbe/AppExtProbe.csproj` (not in slnx, audited separately).
- **Toolchain:** `dotnet 10.0.202`.
- **Project license:** MIT (unchanged).
- **Prior sweep:** `audit-reports/dependency-auditor.md` — 0 CRITICAL / 0 HIGH / 3 MEDIUM (M1 WindowsAppSDK, M2 xunit deprecated, M3 Microsoft.NET.Test.Sdk) / 3 LOW; plus 1 license flag (Microsoft.Web.WebView2 proprietary, permissive redistribution).
- **v2 summary:** **0 CRITICAL, 0 HIGH, 3 MEDIUM, 3 LOW.** All prior findings still reproduce byte-for-byte. **No new dependencies added, none removed, none bumped.** `git log -- '*.csproj' Directory.Build.props` shows no manifest commits since the initial commit.

---

## 1. Change detection since last sweep

| Axis | Status |
|---|---|
| New `<PackageReference>` additions | **None** — diff of `*.csproj` / `Directory.Build.props` between `f12a196` (initial) and `HEAD` is empty. |
| Package version bumps | **None.** |
| Package removals | **None.** |
| New projects | **None.** (`AppExtProbe` still present, still out-of-slnx.) |
| New ecosystems (Node / Python / Go / Rust) | **None detected.** Still single-ecosystem (.NET). |
| Transitive graph drift (same `.nupkg` hashes resolving) | None — identical resolved versions across all projects. |

Conclusion: dependency surface is **frozen** since v1. This sweep is a re-verification of upstream advisory state for the existing pins.

---

## 2. Native tool output (condensed, per project)

### `dotnet list package`

| Project | TFM | Top-level PackageReferences |
|---|---|---|
| `ObsidianQuickNoteTray` | `net10.0-windows7.0` | *(none — consumes via ProjectReference)* |
| `ObsidianQuickNoteWidget.Core` | `net10.0` | *(none — pure library)* |
| `ObsidianQuickNoteWidget` | `net10.0-windows10.0.26100` | `Microsoft.Windows.SDK.BuildTools 10.0.26100.1742`, `Microsoft.WindowsAppSDK 1.6.250205002` |
| `ObsidianQuickNoteWidget.Core.Tests` | `net10.0` | `coverlet.collector 6.0.4`, `Microsoft.NET.Test.Sdk 17.14.1`, `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.4` |
| `tools/AppExtProbe` *(outside slnx)* | `net10.0-windows10.0.22621.0` | `Microsoft.WindowsAppSDK 1.6.250205002`, `Microsoft.Windows.SDK.BuildTools 10.0.26100.1742` |

### `dotnet list package --outdated`

| Project | Package | Requested | Resolved | Latest |
|---|---|---|---|---|
| ObsidianQuickNoteWidget | Microsoft.Windows.SDK.BuildTools | 10.0.26100.1742 | 10.0.26100.1742 | **10.0.28000.1721** |
| ObsidianQuickNoteWidget | Microsoft.WindowsAppSDK | 1.6.250205002 | 1.6.250205002 | **1.8.260317003** |
| Core.Tests | coverlet.collector | 6.0.4 | 6.0.4 | **10.0.0** |
| Core.Tests | Microsoft.NET.Test.Sdk | 17.14.1 | 17.14.1 | **18.4.0** |
| Core.Tests | xunit.runner.visualstudio | 3.1.4 | 3.1.4 | **3.1.5** |
| AppExtProbe | Microsoft.Windows.SDK.BuildTools | 10.0.26100.1742 | 10.0.26100.1742 | **10.0.28000.1721** |
| AppExtProbe | Microsoft.WindowsAppSDK | 1.6.250205002 | 1.6.250205002 | **1.8.260317003** |

`ObsidianQuickNoteTray` and `ObsidianQuickNoteWidget.Core`: *no updates*.

Note: `xunit` 2.9.3 no longer appears in `--outdated` because upstream split v2/v3 into separate package IDs; migration path is tracked by `--deprecated` instead (see below).

### `dotnet list package --deprecated`

| Project | Package | Resolved | Reason | Alternative |
|---|---|---|---|---|
| Core.Tests | **xunit** | 2.9.3 | **Legacy** | `xunit.v3 >= 0.0.0` |

All other projects: no deprecated packages.

### `dotnet list package --vulnerable --include-transitive`

All five projects: **"no vulnerable packages given the current sources"** — direct and transitive clean against the NuGet advisory feed.

---

## 3. Verification table — v1 findings revisited

| ID | Package | v1 finding | v2 state | Δ |
|---|---|---|---|---|
| **M1** | Microsoft.WindowsAppSDK `1.6.250205002` | 2 minor versions behind (latest `1.8.260317003`). Stable-channel gap; widget COM / Widgets3 contract verification required before bump. | **Still 1.6.250205002.** Latest is still `1.8.260317003` (no `1.9` preview promoted to stable since v1). Still no CVE on 1.6.x. Servicing continues, bugfix accumulation unchanged. | **Unchanged. Still MEDIUM.** |
| **M2** | xunit `2.9.3` | Deprecated (Legacy); upstream directs to `xunit.v3`. No CVE. Migration is code-churn, defer. | **Still 2.9.3. Still flagged Legacy** by `dotnet list package --deprecated`. Still no CVE. `xunit.v3` remains the sole upstream-recommended alternative. | **Unchanged. Still MEDIUM.** |
| **M3** | Microsoft.NET.Test.Sdk `17.14.1` | Major-behind (latest `18.x`); no CVE; test-only. | **Still 17.14.1.** Latest is now `18.4.0` (was `18.x` in v1 — minor drift within the major). Still no CVE. | **Unchanged severity; latest tag refreshed 18.x → 18.4.0.** |
| **L1** | Microsoft.Windows.SDK.BuildTools `10.0.26100.1742` | Minor stale → `10.0.28000.1721`. No CVE. | Same pin, same latest (`10.0.28000.1721`). | **Unchanged.** |
| **L2** | coverlet.collector `6.0.4` | Major-behind → `10.0.0`. Test-only. No CVE. | Same pin, same latest (`10.0.0`). | **Unchanged.** |
| **L3** | xunit.runner.visualstudio `3.1.4` | Patch behind → `3.1.5`. No CVE. | Same pin, same latest (`3.1.5`). | **Unchanged — still the cheapest win.** |
| **License flag** | Microsoft.Web.WebView2 (transitive via WindowsAppSDK) | Proprietary Microsoft Software License; redistribution permitted; not OSI. | Same transitive resolved version (`1.0.2651.64`) — WindowsAppSDK pin unchanged, so WebView2 floor unchanged. License terms at `https://aka.ms/webview2/license` unchanged. | **Unchanged. Informational only.** |

---

## 4. New CVEs / advisories since v1

**None discovered in this sweep.**

- `dotnet list package --vulnerable --include-transitive`: clean across all 5 projects.
- No new GHSA/CVE IDs announced against `Microsoft.WindowsAppSDK` 1.6.x, `Microsoft.Web.WebView2` 1.0.2651.64, `Newtonsoft.Json` 13.0.3, `xunit` 2.9.3, or any `Microsoft.TestPlatform.*` 17.14.1 / `Microsoft.CodeCoverage` 17.14.1 surfaced by the NuGet advisory feed at time of sweep.
- Reachability unverified for zero-day advisories not yet in the NuGet feed; if a GHSA is published between sweeps, re-run `dotnet list package --vulnerable --include-transitive` before release.

## 5. License changes since v1

**None.** License matrix is identical to v1:

| Package | License | Status |
|---|---|---|
| Microsoft.WindowsAppSDK, Microsoft.Windows.SDK.BuildTools, Microsoft.NET.Test.Sdk (+ transitives), coverlet.collector, Newtonsoft.Json | MIT | ✅ clean |
| xunit family, xunit.runner.visualstudio | Apache-2.0 | ✅ permissive, MIT-compatible |
| **Microsoft.Web.WebView2** (transitive) | Microsoft Software License (proprietary, not OSI) | ⚠ flagged v1, still flagged — no action required |

No GPL / LGPL / AGPL / SSPL / BUSL / UNLICENSED / unknown introduced.

---

## 6. Top-3 for this cycle

1. **L3 — `xunit.runner.visualstudio 3.1.4 → 3.1.5`.** Patch bump, test-only, zero risk. Cheapest possible win; keep the test runner current so future CVE responses don't compound with unrelated drift.
2. **M1 — plan (don't yet apply) the `Microsoft.WindowsAppSDK 1.6 → 1.8` bump.** Oldest-running MEDIUM and the only one touching **production/runtime**. Stage via `AppExtProbe` (out-of-slnx canary) first, verify `IWidgetProvider2` activation and Widgets3 `UpdateWidgetAsync` behaviour, then promote to the widget project. Release notes: `https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes-archive/`.
3. **M2 — schedule the `xunit 2 → xunit.v3` migration.** Deprecation flag from `dotnet list package --deprecated` will keep firing every sweep until resolved. No CVE, no urgency, but the longer it's deferred the more test code accumulates against v2 namespaces. Budget one cycle for the `Xunit.v3` namespace rewrite + discovery-protocol verification.

---

## 7. Next actions

Read-only sweep complete. No mutations performed. Reply with one of:

- `apply L3` (trivial patch, hand off to manifest-surgeon)
- `apply L1,L3` (bundle trivial bumps)
- `stage M1` (stage WindowsAppSDK 1.6 → 1.8 through AppExtProbe canary first)
- `ignore M2 because xunit.v3 migration is out of scope this cycle`
