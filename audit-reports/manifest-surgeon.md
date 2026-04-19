# manifest-surgeon audit — Package.appxmanifest

**Scope:** `src/ObsidianQuickNoteWidget/Package.appxmanifest` (READ-ONLY)
**Cross-refs:** `src/ObsidianQuickNoteWidget/WidgetIdentifiers.cs`, `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs`, `src/ObsidianQuickNoteWidget/Assets/*`, `src/ObsidianQuickNoteWidget/WidgetsDefinition.xml`
**Verdict:** 🟡 **MOSTLY HEALTHY — one HIGH (silent-drop risk), one MEDIUM (stale sibling file). No CRITICALs; activation path is intact.**

---

## 1. Invariant check matrix

| Invariant | Expected | Actual | Status |
|---|---|---|---|
| Schema well-formed (`[xml]` round-trip) | parses; `Identity.Version` readable | parses; `Version=1.0.0.0` | ✅ |
| `TargetDeviceFamily` Name | `Windows.Desktop` | `Windows.Desktop` | ✅ |
| `TargetDeviceFamily` MinVersion ≥ 22621 | ≥ 10.0.22621.0 | `10.0.22621.0` | ✅ |
| `TargetDeviceFamily` MaxVersionTested | current target | `10.0.26100.0` | ✅ |
| `runFullTrust` capability declared | `<rescap:Capability Name="runFullTrust"/>` | present (line 120) | ✅ |
| `rescap` namespace declared | in `IgnorableNamespaces` | declared + ignorable (line 10) | ✅ |
| `<Executable>` matches publish layout | `ObsidianQuickNoteWidget.exe` at pkg root | `ObsidianQuickNoteWidget.exe` (both `Application/@Executable` and `com:ExeServer/@Executable`) | ✅ |
| Package `Version` reasonable | Major.Minor.Build.Revision, forward-only | `1.0.0.0` (fresh baseline) | ✅ |
| Provider `ProviderIcons` present | Light + Dark icons | both declared (lines 62–63) | ✅ |
| Every Definition has `DisplayName` | non-empty | ObsidianQuickNote ✅, ObsidianRecentNotes ✅ | ✅ |
| Every Definition has `Description` | non-empty | both ✅ | ✅ |
| Every Definition has `<Icons>` | Light + Dark | both ✅ | ✅ |
| Every Definition has `<Screenshots>` (≥1) | ≥1 | both ✅ (line 84, 104) | ✅ |
| Every Definition has `<DarkMode/>` (marker, may be empty) | present | both ✅ (lines 86, 106) | ✅ |
| Every Definition has `<LightMode/>` (marker, may be empty) | present | both ✅ (lines 87, 107) | ✅ |
| Every Definition has `<AdditionalTasks>` | present per spec | **MISSING on both Definitions** | ❌ **HIGH** |

---

## 2. GUID sync-point cross-file verification

All four required sync points are consistent — provider CLSID = **`B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91`**.

| # | Sync point | Location | Value | Match |
|---|---|---|---|---|
| 1 | `[Guid(...)]` on provider class | `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:18` | `[Guid(WidgetIdentifiers.ProviderClsid)]` → `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` | ✅ |
| 2 | `WidgetIdentifiers.ProviderClsid` constant | `src/ObsidianQuickNoteWidget/WidgetIdentifiers.cs:8` | `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` | ✅ |
| 3 | `<com:ComServer>/com:Class/@Id` | `Package.appxmanifest:47` | `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` | ✅ |
| 4 | `<WidgetProvider>/Activation/CreateInstance/@ClassId` | `Package.appxmanifest:66` | `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` | ✅ |

Additionally, `Program.cs:42` resolves `Guid.Parse(WidgetIdentifiers.ProviderClsid)` for `CoRegisterClassObject`, so the runtime registration binds to the same CLSID.

> Note: the spec refers to “`<WidgetProvider>` Id” as the 4th sync point. The `<WidgetProvider>` element itself has no `Id` attribute in this schema; the equivalent GUID carrier is `Activation/CreateInstance/@ClassId` (sync point 4 above). The sibling `AppExtension/@Id="ObsidianQuickNoteWidgetProvider"` is a package-unique *name*, not a GUID, and does not need to match.

---

## 3. Definition ↔ dispatch-switch verification

Manifest Definitions vs `ObsidianWidgetProvider.PushUpdate` dispatch (lines 303–317):

| Definition Id (manifest) | Constant consumed in code | Dispatch branch | Match |
|---|---|---|---|
| `ObsidianQuickNote` | `WidgetIdentifiers.QuickNoteWidgetId` (`ObsidianQuickNote`) | default branch → `CardTemplates.LoadForSize(session.Size)` | ✅ |
| `ObsidianRecentNotes` | `WidgetIdentifiers.RecentNotesWidgetId` (`ObsidianRecentNotes`) | `RecentNotesTemplate` branch | ✅ |

No orphan Definition ids; no orphan code branches.

---

## 4. Icon / screenshot asset resolution

All manifest-referenced assets exist on disk under `src/ObsidianQuickNoteWidget/Assets/`:

| Path in manifest | File on disk | Status |
|---|---|---|
| `Assets\StoreLogo.png` | 515 B | ✅ |
| `Assets\Square150x150Logo.png` | 1584 B | ✅ |
| `Assets\Square44x44Logo.png` | 465 B | ✅ |
| `Assets\Wide310x150Logo.png` | 2692 B | ✅ |
| `Assets\WidgetIcon.light.png` (×4 refs) | 912 B | ✅ |
| `Assets\WidgetIcon.dark.png` (×4 refs) | 937 B | ✅ |
| `Assets\QuickNote.screenshot.png` (×2 refs) | 5446 B | ✅ |

`.csproj` pulls `Assets\**\*.png` with `CopyToOutputDirectory=PreserveNewest`, so they land at the package root under `Assets\` as expected.

---

## 5. Size-list ↔ code-support verification

| Widget | Manifest sizes | Code support | Match |
|---|---|---|---|
| ObsidianQuickNote | `small`, `medium`, `large` | `CardTemplates.LoadForSize` handles `small` → `QuickNote.small.json`, `large` → `QuickNote.large.json`, default → `QuickNote.medium.json` | ✅ (all three sizes have templates) |
| ObsidianRecentNotes | `medium`, `large` | Uses `CardTemplates.RecentNotesTemplate` (size-agnostic) for any size | ✅ |

---

## 6. Findings

### 🟧 HIGH — H1: `<AdditionalTasks>` missing on every `<Definition>`

- **Where:** `Package.appxmanifest`, both Definition blocks (lines 69–89 and 90–109).
- **Why it matters (per manifest-surgeon spec §“How to work / step 2” and guardrails):** "Never drop `<Screenshots>`, `<Icons>`, `<AdditionalTasks>`, `<DisplayName>`, or `<Description>` from any `<Definition>` — Widget Board silently skips widgets missing any of these." Silent-drop class of bug → HIGH.
- **Impact:** On stricter Widget Host versions or with schema-strict validation paths, either (a) the Definition is enumerated but skipped by the Widget Board enumerator, or (b) taskbar-pinnable entry points that the provider later adds cannot be wired.
- **Fix recommendation:** Add an empty `<AdditionalTasks/>` element inside each `<Definition>` after `<ThemeResources>`. Empty is fine — it is a marker, analogous to `<DarkMode/>`/`<LightMode/>`. Schema ordering: `Capabilities` → `ThemeResources` → `AdditionalTasks`. Bump `Identity/@Version` to `1.0.0.1` (minimum) and perform the full uninstall+reinstall dance; do not attempt in-place upgrade for a Definition-shape change.

  ```xml
  <ThemeResources>
    ...
  </ThemeResources>
  <AdditionalTasks />
  ```

### 🟨 MEDIUM — M1: `WidgetsDefinition.xml` is stale and out of sync with manifest

- **Where:** `src/ObsidianQuickNoteWidget/WidgetsDefinition.xml`.
- **Drift:**
  - `ObsidianQuickNote` lists **small, medium** only (manifest: small, medium, large).
  - `ObsidianRecentNotes` lists **large** only (manifest: medium, large) *and* omits the `<Screenshots>` block.
  - Uses `FilePath=` on Icon/Screenshot, whereas the manifest schema uses `Path=`. Different schema (`schemas.microsoft.com/Windows/2022/Widgets`) — a standalone widget-definition file format, not referenced from `.csproj` or the manifest.
- **Why it matters:** The file is not wired into the build (no `<Content>` / `<None>` include, and the Widget Host reads the manifest's `<WidgetProvider>` block, not this file). It is still dangerous as a source of truth confusion — a future contributor may "fix" the manifest to match this stale file and regress the size list.
- **Fix recommendation:** Either delete `WidgetsDefinition.xml` outright, or update it to mirror the manifest (all three sizes for QuickNote, medium+large + Screenshot for RecentNotes) and add a header comment stating it is a reference copy, authoritative source = `Package.appxmanifest`. Prefer **delete**; it is not referenced from the build and cannot be loaded by the Widget Host in its current schema. (Out of scope for this read-only audit — flag for `widget-plumber` or a cleanup PR.)

### 🟩 LOW — L1: Package `Version=1.0.0.0` — no prior release constraint

- **Where:** `Package.appxmanifest:14`.
- **Note:** Fine for an initial release baseline. Reminder: the moment the manifest has shipped to any developer machine via `Add-AppxPackage`, the next change **must** bump this (forward-only) or Windows will refuse to re-register. When applying H1 above, bump to at minimum `1.0.0.1`.

### 🟩 LOW — L2: `Publisher="CN=ObsidianQuickNoteWidgetDev"` is a dev cert subject

- **Where:** `Package.appxmanifest:13`.
- **Note:** Correct for developer sideloading with a self-signed cert. Must be rotated to the real Publisher CN before any Store / production signed distribution, and the signing cert must match exactly or `Add-AppxPackage` fails with `0x800B0109` / similar.

### 🟩 LOW — L3: `AppExtension/@Id="ObsidianQuickNoteWidgetProvider"` + `PublicFolder="Public"`

- **Where:** `Package.appxmanifest:57–58`.
- **Note:** No `Public\` folder exists under `src/ObsidianQuickNoteWidget/`. `PublicFolder` is optional and harmless if the folder is absent (nothing is published cross-package), but it is cruft. Remove the attribute or create an empty `Public\` folder for cleanliness. Non-blocking.

---

## 7. Positive confirmations (no action)

- Schema element ordering preserved; `[xml]` load succeeds; `Package.Identity.Version` round-trips.
- Namespaces `uap`, `uap3`, `uap10`, `com`, `desktop`, `rescap` all declared and in `IgnorableNamespaces` (line 10).
- `com:ExeServer/@Arguments="-RegisterProcessAsComServer"` matches the arg handled in `Program.cs` for `CoRegisterClassObject` startup.
- `Application/@EntryPoint="Windows.FullTrustApplication"` pairs correctly with the `runFullTrust` restricted capability.
- `ProviderIcons` (light + dark) are declared at the `<WidgetProvider>` scope in addition to per-Definition `<Icons>` — Widget Board fallback chain is intact.
- `.csproj` `TargetPlatformMinVersion` (`10.0.22621.0`) agrees with manifest `TargetDeviceFamily/@MinVersion`.

---

## 8. Top issues (ranked)

1. **HIGH — H1:** Add `<AdditionalTasks/>` to both `<Definition>` blocks + version bump + uninstall/reinstall.
2. **MEDIUM — M1:** Delete or resync the stale `WidgetsDefinition.xml`; it contradicts the manifest on sizes.
3. **LOW — L1/L2/L3:** Version bump discipline going forward; rotate Publisher CN before release; drop unused `PublicFolder="Public"`.

**No CRITICAL findings.** Activation path (CLSID registration, FullTrust capability, Executable path, TargetDeviceFamily, Definition↔dispatch wiring) is complete and self-consistent.
