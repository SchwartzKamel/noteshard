# manifest-surgeon audit v3 — Package.appxmanifest

**Scope:** `src/ObsidianQuickNoteWidget/Package.appxmanifest` (READ-ONLY re-sweep)
**Cross-refs:** `src/ObsidianQuickNoteWidget/WidgetIdentifiers.cs`, `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs`
**Priors:** `audit-reports/manifest-surgeon.md` (v1 — H1/M1 fixed), `audit-reports/v2/manifest-surgeon.md` (v2 — 🟢 healthy at `1.0.0.1`)
**Verdict:** 🟢 **HEALTHY — no new manifest churn since v2 except the expected forward version bump (`1.0.0.1` → `1.0.0.2`). All v2 invariants still hold.**

---

## 1. Delta since v2

| Element | v2 value | v3 value | Delta |
|---|---|---|---|
| `Identity/@Version` | `1.0.0.1` | `1.0.0.2` | ✅ **Forward-only bump (+1 revision).** Expected cadence for next reinstall; satisfies the "never backward, never unchanged" guardrail. |
| `Identity/@Name` | `ObsidianQuickNoteWidget` | `ObsidianQuickNoteWidget` | unchanged |
| `Identity/@Publisher` | `CN=ObsidianQuickNoteWidgetDev` | `CN=ObsidianQuickNoteWidgetDev` | unchanged (dev cert, still acceptable per scope) |
| `TargetDeviceFamily` MinVersion / MaxVersionTested | `10.0.22621.0` / `10.0.26100.0` | `10.0.22621.0` / `10.0.26100.0` | unchanged |
| Provider CLSID (all four sync points) | `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` | same | unchanged |
| `<Definition>` shape (Capabilities → ThemeResources → AdditionalTasks) × 2 | complete | complete | unchanged |
| `<AdditionalTasks/>` per Definition | present (lines 89, 110) | present (lines 89, 110) | unchanged |
| `<Executable>` references (×2) | `ObsidianQuickNoteWidget.exe` | `ObsidianQuickNoteWidget.exe` | unchanged |
| `<rescap:Capability Name="runFullTrust"/>` | present | present | unchanged |
| `uap3:AppExtension/@Name` contract | `com.microsoft.windows.widgets` | `com.microsoft.windows.widgets` | unchanged |

**Line count / XML schema:** no structural churn. `[xml]` round-trip clean; `$m.Package.Identity.Version` → `1.0.0.2`.

---

## 2. Required v3 invariant re-check

| # | Invariant | Expected | Actual | Status |
|---|---|---|---|---|
| 1 | `Identity/@Version` | `1.0.0.2` | `1.0.0.2` (line 14) | ✅ |
| 2 | CLSID sync point #1 — `[Guid]` on provider class | `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` | `Providers/ObsidianWidgetProvider.cs` `[Guid(WidgetIdentifiers.ProviderClsid)]` | ✅ |
| 3 | CLSID sync point #2 — `WidgetIdentifiers.ProviderClsid` | same | `WidgetIdentifiers.cs:8` → `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` | ✅ |
| 4 | CLSID sync point #3 — `<com:ComServer>/com:Class/@Id` | same | manifest line 47 | ✅ |
| 5 | CLSID sync point #4 — `<WidgetProvider>/Activation/CreateInstance/@ClassId` | same | manifest line 66 | ✅ |
| 6 | `<AdditionalTasks/>` in both `<Definition>` blocks | present | ObsidianQuickNote (line 89), ObsidianRecentNotes (line 110) | ✅ |
| 7 | `TargetDeviceFamily/@MinVersion` ≥ `10.0.22621.0` | ≥ 22621 | `10.0.22621.0` (line 23) | ✅ |
| 8 | `TargetDeviceFamily/@MaxVersionTested` | current target | `10.0.26100.0` (line 23) | ✅ |
| 9 | Per-Definition `<Screenshots>` ≥ 1 | both | lines 84, 105 | ✅ |
| 10 | Per-Definition empty `<DarkMode/>` / `<LightMode/>` markers | both | lines 86/87, 107/108 | ✅ |
| 11 | `runFullTrust` capability declared + `rescap` ignorable | yes | lines 9, 10, 122 | ✅ |
| 12 | `<Executable>` matches publish layout (exe at package root) | `ObsidianQuickNoteWidget.exe` | lines 31, 44 — identical casing | ✅ |
| 13 | AppExtension contract name | `com.microsoft.windows.widgets` | line 55 — exact | ✅ |

All 13 invariants pass. Four CLSID sync points carry the single shared GUID end-to-end.

---

## 3. No-churn confirmation

- No new `<Definition>` added or removed; no size-list change.
- No CLSID rotation.
- No `<Executable>` rename.
- No capability added/removed.
- No namespace / `IgnorableNamespaces` change.
- No `MinVersion` / `MaxVersionTested` change.
- Only diff relative to v2: `Version="1.0.0.1"` → `Version="1.0.0.2"`. This is a revision-only bump — safe with in-place `Add-AppxPackage` **only if** no Definition shape changed (it did not).

---

## 4. Reinstall guidance

Since only `Identity/@Version` moved and Definition shape is unchanged since v2, an in-place upgrade (`Add-AppxPackage` with the new MSIX, no prior `Remove-AppxPackage`) is permissible for this revision. A full uninstall+reinstall dance is **not required** for the `1.0.0.1 → 1.0.0.2` hop. Pinned instances (if any) will survive.

---

## 5. Carry-over LOW / INFO (unchanged, non-blocking)

- **LOW L2:** `Publisher="CN=ObsidianQuickNoteWidgetDev"` — dev cert, must rotate before Store/production.
- **LOW L3:** `uap3:AppExtension/@PublicFolder="Public"` declared but no `Public\` folder on disk — cosmetic, harmless.
- **INFO I1:** stale `WidgetsDefinition.xml` copies may still sit under `bin\` from pre-deletion builds; `dotnet clean` wipes them. No packaging impact.

---

## 6. Top 3 (ranked)

1. *(none blocking — manifest shippable for dev sideload at `1.0.0.2`)*
2. **LOW L2 (carry-over):** rotate `Publisher` CN to production cert subject before Store signing.
3. **LOW L3 / INFO I1 (carry-over):** drop the unused `PublicFolder="Public"` attribute *or* create an empty `Public\` folder; run `dotnet clean` to purge stale `WidgetsDefinition.xml` outputs under `bin\` before any packaging dry-run.

---

## 7. Sign-off

v3 is a clean forward version bump (`1.0.0.1` → `1.0.0.2`) with **zero structural manifest churn**. All four CLSID sync points still aligned on `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91`. `<AdditionalTasks/>` present on both Definitions. `MinVersion` ≥ `10.0.22621.0`. `runFullTrust` + `Windows.FullTrustApplication` + `com:ExeServer` triangle intact. AppExtension contract name `com.microsoft.windows.widgets` exact. Ready for reinstall — in-place `Add-AppxPackage` acceptable for this revision.
