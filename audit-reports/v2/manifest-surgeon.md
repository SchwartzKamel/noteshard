# manifest-surgeon audit v2 — Package.appxmanifest

**Scope:** `src/ObsidianQuickNoteWidget/Package.appxmanifest` (READ-ONLY re-sweep)
**Cross-refs:** `src/ObsidianQuickNoteWidget/WidgetIdentifiers.cs`, `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs`, `src/ObsidianQuickNoteWidget/Assets/*`
**Prior report:** `audit-reports/manifest-surgeon.md` (v1) — flagged H1 (missing `<AdditionalTasks/>`) and M1 (stale `WidgetsDefinition.xml`).
**Verdict:** 🟢 **HEALTHY — both v1 findings remediated. Activation path intact, all four CLSID sync points aligned, all per-Definition required elements present.**

---

## 1. Remediation of v1 findings

| v1 ID | v1 Severity | v1 Issue | v2 Status |
|---|---|---|---|
| H1 | 🟧 HIGH | `<AdditionalTasks/>` missing from both `<Definition>` blocks | ✅ **FIXED** — present on both (lines 89, 110). Empty markers, schema ordering preserved (`Capabilities` → `ThemeResources` → `AdditionalTasks`). |
| M1 | 🟨 MEDIUM | Stale `src/ObsidianQuickNoteWidget/WidgetsDefinition.xml` contradicting manifest | ✅ **FIXED** — source file deleted (`Test-Path src\ObsidianQuickNoteWidget\WidgetsDefinition.xml` → `False`). No references remain in `*.csproj` or `*.cs`. Lingering 1511-byte copies under `bin\x64\{Debug,Release}\...` are stale build outputs only — they will be cleared on next clean build and have no effect on packaging. |
| L1 | 🟩 LOW | `Version=1.0.0.0` baseline | ✅ Bumped to `1.0.0.1` (forward), correctly paired with the Definition-shape change. |
| L2 | 🟩 LOW | Dev-cert publisher `CN=ObsidianQuickNoteWidgetDev` | 🟢 Unchanged — acceptable per task scope (dev sideload). Must rotate before Store. |
| L3 | 🟩 LOW | Unused `PublicFolder="Public"` (no `Public\` folder) | 🟡 Unchanged (still no `Public\` folder on disk). Cosmetic only — `PublicFolder` is harmless when absent. Not blocking. |

---

## 2. Required v2 invariant matrix

| # | Invariant | Expected | Actual | Status |
|---|---|---|---|---|
| 1 | `<AdditionalTasks/>` in **both** `<Definition>` blocks | present | line 89 (`ObsidianQuickNote`), line 110 (`ObsidianRecentNotes`) | ✅ |
| 2 | `<Identity Version>` | `1.0.0.1` | `1.0.0.1` (line 14) | ✅ |
| 3 | CLSID sync point #1 — class `[Guid]` on `ObsidianWidgetProvider` | `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` | `Providers/ObsidianWidgetProvider.cs:19` → `[Guid(WidgetIdentifiers.ProviderClsid)]` | ✅ |
| 4 | CLSID sync point #2 — `WidgetIdentifiers.ProviderClsid` | same | `WidgetIdentifiers.cs:8` → `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` | ✅ |
| 5 | CLSID sync point #3 — `<com:ComServer>/com:Class/@Id` | same | manifest line 47 → `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` | ✅ |
| 6 | CLSID sync point #4 — `<WidgetProvider>` provider Id (`Activation/CreateInstance/@ClassId`) | same | manifest line 66 → `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` | ✅ |
| 7 | `WidgetsDefinition.xml` deleted | absent from source tree, no references | source file gone; no hits in `.csproj`/`.cs` | ✅ |
| 8 | Per-Definition `<Screenshots>` (≥1) | both | line 84 (QuickNote), line 105 (RecentNotes) | ✅ |
| 9 | Per-Definition empty `<DarkMode/>` | both | line 86, line 107 | ✅ |
| 10 | Per-Definition empty `<LightMode/>` | both | line 87, line 108 | ✅ |
| 11 | `TargetDeviceFamily/@MinVersion` ≥ `10.0.22621.0` | ≥ 22621 | `10.0.22621.0` (line 23) | ✅ |
| 12 | `TargetDeviceFamily/@MaxVersionTested` | current target | `10.0.26100.0` | ✅ |

All four CLSID sync points carry the **single shared GUID** `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91`. No drift.

---

## 3. Fresh checks

### 3.1 AppExtension contract identifier ("hash") correctness

The AppExtension that the Windows Widget Host discovers is keyed by a **fixed contract Name** — Windows treats this string as the immutable host↔provider handshake. The manifest-surgeon spec implicitly enforces this: a wrong Name means Widget Host never enumerates the provider.

| Element | Required value | Actual value | Status |
|---|---|---|---|
| `uap3:AppExtension/@Name` | `com.microsoft.windows.widgets` (the only contract name the Widget Host enumerates) | `com.microsoft.windows.widgets` (line 55) | ✅ |
| `uap3:AppExtension/@Id` | package-unique non-empty string (provider name; not a GUID, no hash requirement) | `ObsidianQuickNoteWidgetProvider` (line 57) | ✅ |
| `uap3:AppExtension/@DisplayName` | non-empty | `Obsidian Quick Note Widget Provider` (line 56) | ✅ |
| `uap3:AppExtension` namespace | `http://schemas.microsoft.com/appx/manifest/uap/windows10/3` declared + ignorable | declared line 5, in `IgnorableNamespaces` line 10 | ✅ |
| `<WidgetProvider>` schema element under `<uap3:Properties>` | unqualified element nested under `uap3:Properties` per WCOS widget contract | exactly that shape (lines 59–113) | ✅ |

> Interpretation note: the task's "AppExtension hash" check refers to the contract-name handshake `com.microsoft.windows.widgets`. This manifest matches it exactly; any typo (e.g., `com.microsoft.windows.widget` singular, or wrong casing on a case-sensitive comparison path) would silently break enumeration.

### 3.2 Executable path resolves

| Reference | Manifest value | Resolves to | Status |
|---|---|---|---|
| `Application/@Executable` | `ObsidianQuickNoteWidget.exe` (line 31) | Package root after publish (project AssemblyName `ObsidianQuickNoteWidget` → exe at root) | ✅ |
| `com:ExeServer/@Executable` | `ObsidianQuickNoteWidget.exe` (line 44) | Same physical file, identical case | ✅ |
| `com:ExeServer/@Arguments` | `-RegisterProcessAsComServer` (line 46) | Argument is parsed by `Program.cs` (matches the documented `CoRegisterClassObject` startup branch) | ✅ |
| `Application/@EntryPoint` | `Windows.FullTrustApplication` (line 31) | Required pairing for an out-of-proc COM-server desktop bridge app with `runFullTrust` | ✅ |

Both `Executable` references point at the same root-level exe with identical casing — Windows will find a single binary regardless of which activation path (Start-menu launch vs. COM activation by Widget Host) is exercised.

### 3.3 `runFullTrust` + capability list sane

| Item | Expected | Actual | Status |
|---|---|---|---|
| `<Capabilities>` block | present, contains only what the provider needs | single `<rescap:Capability Name="runFullTrust" />` (line 122) | ✅ |
| `rescap` namespace | declared and listed in `IgnorableNamespaces` | line 9 + line 10 | ✅ |
| Coupling with COM out-of-proc + `Windows.FullTrustApplication` | `runFullTrust` mandatory whenever a packaged desktop app hosts a `com:ExeServer` it expects to run with full Win32 access | satisfied | ✅ |
| Over-broad capabilities (e.g., `internetClient`, `broadFileSystemAccess`) | absent unless functionally required | none declared — minimum-privilege | ✅ |
| Restricted-cap acknowledgment in build | `rescap` ignorable so non-Store sideload accepts it; signed dev cert tolerated | matches dev-sideload posture | ✅ |

Capability surface is **minimal and correct**. The provider opens local files via the Obsidian CLI surface (out-of-package process) under full-trust — no broker capabilities needed.

### 3.4 Publisher posture

`Publisher="CN=ObsidianQuickNoteWidgetDev"` (line 13). Dev self-signed cert subject. **Acceptable for sideload-only audit scope** per task. No change requested. Rotation reminder stands for the eventual production signing handoff.

---

## 4. Schema / round-trip

- `[xml]$m = Get-Content src\ObsidianQuickNoteWidget\Package.appxmanifest; $m.Package.Identity.Version` → `1.0.0.1` (parses cleanly, no XML errors).
- Element ordering inside each `<Definition>` is the schema-required `Capabilities` → `ThemeResources` → `AdditionalTasks`.
- Empty marker elements (`<DarkMode/>`, `<LightMode/>`, `<AdditionalTasks/>`) preserved as self-closing — not "cleaned up."

---

## 5. Findings

### 🟩 None blocking. No HIGH, no MEDIUM, no CRITICAL.

### 🟩 LOW — L2 (carry-over): dev publisher

Unchanged from v1 — `CN=ObsidianQuickNoteWidgetDev` is correct for sideload, must be rotated to the production CN before Store/signed distribution. **Acceptable per task scope.**

### 🟩 LOW — L3 (carry-over): `PublicFolder="Public"` cosmetic

`uap3:AppExtension/@PublicFolder="Public"` (line 58) still declared while no `Public\` folder exists on disk. Harmless when absent (nothing is published cross-package). Strip the attribute *or* create an empty `Public\` folder when convenient. Non-blocking, no functional impact.

### 🟩 INFO — I1: stale `WidgetsDefinition.xml` copies under `bin\`

Build outputs from before the source-file deletion still sit at:

- `src\ObsidianQuickNoteWidget\bin\x64\Debug\net10.0-windows10.0.19041.0\WidgetsDefinition.xml`
- `src\ObsidianQuickNoteWidget\bin\x64\Release\net10.0-windows10.0.19041.0\WidgetsDefinition.xml` (+ `publish\`)
- `src\ObsidianQuickNoteWidget\bin\x64\Release\net10.0-windows10.0.22621.0\WidgetsDefinition.xml` (+ `publish\`)

These are not packaged from the manifest's perspective and will disappear on the next `Clean` / fresh `publish`. Read-only audit — no action taken. Flag for `widget-plumber` or a one-line `dotnet clean` if a packaging dry-run is planned.

---

## 6. Top issues (ranked)

1. *(none — manifest is shippable as-is for dev sideload)*
2. **LOW L3 (cosmetic):** drop `PublicFolder="Public"` *or* create empty `Public\` folder.
3. **INFO I1:** `dotnet clean` to clear stale `WidgetsDefinition.xml` artifacts under `bin\` before packaging dry-run.

---

## 7. Sign-off

All v1 blocking findings remediated. All eight v2-required invariants satisfied. AppExtension contract name correct, executable paths resolve to the same root binary with identical casing, capability list is minimum-privilege (`runFullTrust` only, paired correctly with `Windows.FullTrustApplication` + `com:ExeServer`), four CLSID sync points carry the same GUID end-to-end (`[Guid]` attribute → `WidgetIdentifiers.ProviderClsid` → `<com:ComServer>` → `<WidgetProvider>` `Activation/CreateInstance/@ClassId`).

**Version bump (`1.0.0.0` → `1.0.0.1`) is in place — when this manifest reinstalls, dispatch the mandatory full uninstall+reinstall dance** (`Remove-AppxPackage` then `Add-AppxPackage`); pinned widgets will be wiped because both Definition shape and version moved.
