# Dependency Audit — ObsidianQuickNoteWidget

- **Scope:** all `.csproj` files in this repo (.NET 10 / net10.0 + net10.0-windows).
- **Tools run (read-only):** `dotnet --version` (10.0.202), `dotnet restore`, `dotnet list package --outdated`, `dotnet list package --vulnerable`, `dotnet list package --vulnerable --include-transitive`, `dotnet list package --deprecated`, `dotnet list package --include-transitive`.
- **Project license:** MIT (see `LICENSE` at repo root).
- **Summary:** **0 CRITICAL, 0 HIGH, 3 MEDIUM, 2 LOW** across .NET (single ecosystem). No known CVEs, no GPL/AGPL contamination, no unused direct references.

---

## 1. Inventory — every `<PackageReference>`

| Project | Package | Version | Role |
|---|---|---|---|
| `src/ObsidianQuickNoteWidget` | Microsoft.WindowsAppSDK | 1.6.250205002 | runtime (prod) |
| `src/ObsidianQuickNoteWidget` | Microsoft.Windows.SDK.BuildTools | 10.0.26100.1742 | build-time |
| `tools/AppExtProbe` | Microsoft.WindowsAppSDK | 1.6.250205002 | tools (non-shipping) |
| `tools/AppExtProbe` | Microsoft.Windows.SDK.BuildTools | 10.0.26100.1742 | build-time (tools) |
| `tests/ObsidianQuickNoteWidget.Core.Tests` | coverlet.collector | 6.0.4 | test |
| `tests/ObsidianQuickNoteWidget.Core.Tests` | Microsoft.NET.Test.Sdk | 17.14.1 | test |
| `tests/ObsidianQuickNoteWidget.Core.Tests` | xunit | 2.9.3 | test |
| `tests/ObsidianQuickNoteWidget.Core.Tests` | xunit.runner.visualstudio | 3.1.4 | test |

Projects with **no** `PackageReference`: `src/ObsidianQuickNoteWidget.Core`, `src/ObsidianQuickNoteTray` (consume deps only via `ProjectReference`).

### Notable transitive packages (resolved)

| Package | Resolved | Pulled in by | Notes |
|---|---|---|---|
| Microsoft.Web.WebView2 | 1.0.2651.64 | Microsoft.WindowsAppSDK | **Proprietary Microsoft Software License** (not OSI). Permissive redistribution via NuGet; see §4. |
| Microsoft.CodeCoverage | 17.14.1 | Microsoft.NET.Test.Sdk | MIT |
| Microsoft.TestPlatform.ObjectModel | 17.14.1 | Microsoft.NET.Test.Sdk | MIT |
| Microsoft.TestPlatform.TestHost | 17.14.1 | Microsoft.NET.Test.Sdk | MIT |
| Newtonsoft.Json | 13.0.3 | Microsoft.TestPlatform.* | MIT. Last 13.0.3 has no known CVEs. |
| xunit.abstractions | 2.0.3 | xunit | Apache-2.0 |
| xunit.analyzers | 1.18.0 | xunit | Apache-2.0 |
| xunit.assert / core / extensibility.* | 2.9.3 | xunit | Apache-2.0 |

---

## 2. Native tool output (condensed)

- `dotnet list package --vulnerable` → **no vulnerable packages** in any project (direct).
- `dotnet list package --vulnerable --include-transitive` → **no vulnerable packages** (direct or transitive).
- `dotnet list package --deprecated` → `xunit 2.9.3` flagged **Legacy** (alternative: `xunit.v3 >= 0.0.0`).
- `dotnet list package --outdated` → see §3.

---

## 3. Findings

### CRITICAL
None. No CVEs in any runtime dependency.

### HIGH
None. No CVEs in any dev/test dependency.

### MEDIUM (stale + no CVE)

| # | Package | Project | Current → Latest | Jump | Notes / Breaking-change risk |
|---|---|---|---|---|---|
| M1 | **Microsoft.WindowsAppSDK** | widget (prod) + AppExtProbe | `1.6.250205002` → `1.8.260317003` | minor×2 (1.6 → 1.7 → 1.8) | Pinned at 1.6 stable (Feb 2025). 1.7 (Apr 2025) and 1.8 (Mar 2026 stable) have been released. Servicing for 1.6 continues but missed bugfixes accumulate. **Breaking-change risk for THIS repo (widget COM server / Widgets3):** 1.7 added Widgets3 `UpdateWidgetAsync` overloads; 1.8 tightened `IWidgetProvider2` activation contract and touched MSIX self-contained layout. Any upgrade requires re-verifying COM activation and the widget JSON template contract. Also bumps the transitive WebView2 floor. Do **not** auto-bump. Release notes: <https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes-archive/>. |
| M2 | **xunit** (deprecated: Legacy) | tests | `2.9.3` → `3.x` (`xunit.v3`) | major (package rename) | `dotnet` flags this as **Deprecated / Legacy**. Upstream recommends migrating to the new `xunit.v3` package family. v2 still receives maintenance and has no CVE, but is EOL-bound. Migration requires code changes (new `Xunit.v3` namespace, different test-discovery protocol). Defer unless/until upstream drops maintenance. Ref: <https://xunit.net/docs/getting-started/v3/migration>. |
| M3 | **Microsoft.NET.Test.Sdk** | tests | `17.14.1` → `18.4.0` | major | New major line (18.x). Contains bugfixes for .NET 10 test host. No CVE. Safe to bump, but a major — verify test discovery under VS and `dotnet test` after. |

### LOW (cosmetic)

| # | Package | Project | Current → Latest | Jump | Notes |
|---|---|---|---|---|---|
| L1 | Microsoft.Windows.SDK.BuildTools | widget + AppExtProbe | `10.0.26100.1742` → `10.0.28000.1721` | minor | Tracks a newer Windows SDK (26100 → 28000). Harmless cosmetic drift unless you need APIs from the newer SDK. No CVE. |
| L2 | coverlet.collector | tests | `6.0.4` → `10.0.0` | major | Jumped major version line. Test-only tool. Upgrade when convenient; verify coverage output format in CI. No CVE. |
| L3 | xunit.runner.visualstudio | tests | `3.1.4` → `3.1.5` | patch | Trivial patch bump. No CVE. |

---

## 4. License audit

Repo license: **MIT**. No GPL / LGPL / AGPL / SSPL / BUSL detected.

| Package | License | Concern? |
|---|---|---|
| Microsoft.WindowsAppSDK | MIT | ✅ clean |
| Microsoft.Windows.SDK.BuildTools | MIT | ✅ clean |
| Microsoft.NET.Test.Sdk (+ transitives Microsoft.CodeCoverage, TestPlatform.*) | MIT | ✅ clean |
| coverlet.collector | MIT | ✅ clean |
| xunit (+ transitives: abstractions, analyzers, assert, core, extensibility.*) | Apache-2.0 | ✅ permissive, MIT-compatible |
| xunit.runner.visualstudio | Apache-2.0 | ✅ permissive |
| Newtonsoft.Json (transitive) | MIT | ✅ clean |
| **Microsoft.Web.WebView2** (transitive via WindowsAppSDK) | **Microsoft Software License (proprietary, not OSI)** | ⚠ **Flag.** Not copyleft, no contamination of our MIT code, and Microsoft's license explicitly permits redistribution as part of an app. However it is **not an OSI-approved open-source license** — if the project ever claims "100% OSI-licensed deps," this is the exception. Actionable only if you add a SBOM/license-policy check (e.g., `dotnet-project-licenses`). No action required today. License text: <https://aka.ms/webview2/license>. |

No AGPL, no GPL, no LGPL, no unknown licenses.

---

## 5. Unused / duplicate / risky transitive

- **Unused direct references:** none.
  - `Microsoft.WindowsAppSDK` — consumed by `src/ObsidianQuickNoteWidget/Com/ClassFactory.cs`, `Providers/ObsidianWidgetProvider.cs`, and `tools/AppExtProbe/Program.cs`. ✅ used.
  - `Microsoft.Windows.SDK.BuildTools` — required build-time tooling for CsWinRT projection / WinRT metadata referenced by WindowsAppSDK consumers. ✅ used.
  - Test packages (`xunit`, `xunit.runner.visualstudio`, `coverlet.collector`, `Microsoft.NET.Test.Sdk`) — all required by the xUnit test runner / coverage pipeline. ✅ used.
- **Duplicate versions in graph:** none detected. Every transitive resolves to a single version.
- **Risky transitives:** none flagged by `--vulnerable --include-transitive`. Microsoft.Web.WebView2 `1.0.2651.64` has no known CVEs; a 1.0.30xx line exists and will arrive automatically on the next `Microsoft.WindowsAppSDK` bump.

---

## 6. Suggested upgrade order (safe → risky)

1. **L3** `xunit.runner.visualstudio 3.1.4 → 3.1.5` (patch, test-only, zero risk).
2. **L1** `Microsoft.Windows.SDK.BuildTools 10.0.26100.1742 → 10.0.28000.1721` (build-time only).
3. **L2** `coverlet.collector 6.0.4 → 10.0.0` (test-only major; verify coverage XML output in CI).
4. **M3** `Microsoft.NET.Test.Sdk 17.14.1 → 18.4.0` (test-only major; run `dotnet test` after).
5. **M1** `Microsoft.WindowsAppSDK 1.6.250205002 → 1.8.260317003` — **prod, requires manual verification** of widget COM activation, `IWidgetProvider2` contract, Widgets3 host handshake, and MSIX self-contained layout. Do the AppExtProbe project first as a canary, then the widget. Read 1.7 + 1.8 release notes end-to-end before bumping. **Do not auto-bump.**
6. **M2** `xunit 2.9.3 → xunit.v3` — deprecation-driven migration, code changes required. Defer until a release cycle can absorb the churn.

---

## 7. Next actions

Read-only audit complete. No mutations performed.

Reply with one of:
- `apply L3` (safe patch, I'll hand off to manifest-surgeon)
- `apply L1,L3` (bundle the trivial bumps)
- `apply M1` (will stage the WindowsAppSDK 1.6 → 1.8 bump with build+test verification)
- `ignore M2 because xunit.v3 migration is out of scope this cycle`
