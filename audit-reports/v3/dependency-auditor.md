# Dependency Audit v3 — ObsidianQuickNoteWidget

- **Mode:** READ-ONLY verification sweep. No manifests or lockfiles mutated.
- **Scope:** all 4 projects in `ObsidianQuickNoteWidget.slnx` (`ObsidianQuickNoteTray`, `ObsidianQuickNoteWidget.Core`, `ObsidianQuickNoteWidget`, `ObsidianQuickNoteWidget.Core.Tests`) + `tools/AppExtProbe/AppExtProbe.csproj` (out-of-slnx).
- **Toolchain:** `dotnet 10.0.202`.
- **Project license:** MIT (unchanged).
- **Prior sweeps:** v1 `audit-reports/dependency-auditor.md`, v2 `audit-reports/v2/dependency-auditor.md` — both landed 0 CRITICAL / 0 HIGH / 3 MEDIUM / 3 LOW + 1 informational license flag.
- **v3 summary:** **0 CRITICAL, 0 HIGH, 3 MEDIUM, 3 LOW.** Dependency surface still **frozen** — `git diff 750f032 HEAD -- '*.csproj' Directory.Build.props Directory.Packages.props` is empty. Commit `750f032` (folder dropdown + new-folder text input) was UI + provider code only, zero manifest churn. Every upstream `Latest` tag is **identical to v2** — not even cosmetic drift since the last sweep.

---

## 1. Delta table vs v2

| ID | Package | v2 state | v3 state | Δ |
|---|---|---|---|---|
| **M1** | Microsoft.WindowsAppSDK (prod + AppExtProbe) | `1.6.250205002` → latest `1.8.260317003` | `1.6.250205002` → latest `1.8.260317003` | **None.** Still 2 minor versions behind. No new stable promotion. |
| **M2** | xunit (tests) | `2.9.3` flagged **Legacy**; alt `xunit.v3 >= 0.0.0` | `2.9.3` flagged **Legacy**; alt `xunit.v3 >= 0.0.0` | **None.** Deprecation still fires. |
| **M3** | Microsoft.NET.Test.Sdk (tests) | `17.14.1` → latest `18.4.0` | `17.14.1` → latest `18.4.0` | **None** (v1 latest `18.x` refreshed to `18.4.0` in v2; no further refresh). |
| **L1** | Microsoft.Windows.SDK.BuildTools (widget + AppExtProbe) | `10.0.26100.1742` → latest `10.0.28000.1721` | same | **None.** |
| **L2** | coverlet.collector (tests) | `6.0.4` → latest `10.0.0` | same | **None.** |
| **L3** | xunit.runner.visualstudio (tests) | `3.1.4` → latest `3.1.5` | same | **None.** Cheapest win still unclaimed. |
| **License flag** | Microsoft.Web.WebView2 (transitive) | `1.0.2651.64`, proprietary MSL | same transitive resolve | **None.** |
| Inventory — new `<PackageReference>` | 0 additions | 0 additions | **None.** |
| Inventory — removals | 0 | 0 | **None.** |
| Inventory — version bumps | 0 | 0 | **None.** |
| Ecosystems | .NET only | .NET only | **None.** |

### Native tool output (raw, condensed)

- `dotnet list <slnx|AppExtProbe.csproj> package --outdated` → identical table to v2: widget project flags `Microsoft.Windows.SDK.BuildTools`, `Microsoft.WindowsAppSDK`; Core.Tests flags `coverlet.collector`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`; AppExtProbe flags the two SDK packages. `ObsidianQuickNoteTray` and `ObsidianQuickNoteWidget.Core`: no updates.
- `dotnet list <…> package --deprecated` → only `xunit 2.9.3` (Legacy) in Core.Tests; everything else clean.
- `dotnet list <…> package --vulnerable --include-transitive` → **no vulnerable packages given the current sources** across all 5 projects (direct and transitive).

---

## 2. New CVEs / advisories since v2

**None.**

- NuGet advisory feed: `--vulnerable --include-transitive` clean across all five projects at sweep time.
- No new GHSA/CVE IDs observed against `Microsoft.WindowsAppSDK 1.6.x`, `Microsoft.Web.WebView2 1.0.2651.64`, `Newtonsoft.Json 13.0.3`, `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.4`, `Microsoft.NET.Test.Sdk / Microsoft.TestPlatform.* / Microsoft.CodeCoverage 17.14.1`, `coverlet.collector 6.0.4`, or `Microsoft.Windows.SDK.BuildTools 10.0.26100.1742` between v2 and v3.
- Reachability caveat: zero-days not yet published to the NuGet advisory feed cannot be detected by `dotnet list`; re-run `--vulnerable --include-transitive` immediately pre-release.

## 3. License changes since v2

**None.** Matrix identical to v1/v2: all direct deps MIT or Apache-2.0; sole flag remains `Microsoft.Web.WebView2` (transitive, proprietary Microsoft Software License, permissive redistribution, informational only). No GPL / LGPL / AGPL / SSPL / BUSL / UNLICENSED introduced.

---

## 4. Top-3 for this cycle

Priorities unchanged from v2 — three consecutive read-only sweeps with zero dependency movement means the backlog is the signal, not new findings.

1. **L3 — `xunit.runner.visualstudio 3.1.4 → 3.1.5`.** Patch bump, test-only, zero-risk. Has now survived two full sweeps unclaimed; grab it next cycle to stop compounding drift on the test runner.
2. **M1 — stage `Microsoft.WindowsAppSDK 1.6.250205002 → 1.8.260317003`** through the `AppExtProbe` out-of-slnx canary first (verify `IWidgetProvider2` activation + Widgets3 `UpdateWidgetAsync` + MSIX self-contained layout), then promote to the widget project. Still the only MEDIUM touching **production runtime**; the 1.6 → 1.8 gap is not shrinking on its own.
3. **M2 — schedule the `xunit 2 → xunit.v3` migration.** The `--deprecated` flag will fire every sweep until resolved; no CVE but defer-cost is ongoing test-code accumulation against v2 namespaces. Budget one cycle for the `Xunit.v3` namespace rewrite + discovery-protocol verification.

---

## 5. Next actions

Read-only sweep complete. No mutations performed. Reply with one of:

- `apply L3` (trivial patch, hand off to manifest-surgeon)
- `apply L1,L3` (bundle trivial bumps)
- `stage M1` (canary WindowsAppSDK 1.6 → 1.8 through AppExtProbe first)
- `ignore M2 because xunit.v3 migration is out of scope this cycle`
