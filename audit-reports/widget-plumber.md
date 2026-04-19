# widget-plumber audit

**Scope:** COM / WinRT / STA-pump plumbing for `ObsidianQuickNoteWidget`.
**Mode:** READ-ONLY. No source files modified.
**Date:** automated sweep.

---

## 1. Four-point GUID sync — ✅ CLEAN

| # | Sync point | Location | Value |
|---|---|---|---|
| 1 | `[Guid(...)]` on provider class | `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:18` | `WidgetIdentifiers.ProviderClsid` → `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` |
| 2 | `WidgetIdentifiers.ProviderClsid` constant | `src/ObsidianQuickNoteWidget/WidgetIdentifiers.cs:8` | `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` |
| 3 | Manifest `<com:Class Id=…>` | `Package.appxmanifest:47` | `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` |
| 4 | Manifest `<CreateInstance ClassId=…>` | `Package.appxmanifest:66` | `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91` |

All four byte-exact and casing-exact (upper-case hex, canonical hyphenation). `Guid.Parse` in `Program.cs:42` is case-insensitive anyway, but consistency is good.

The `<uap3:AppExtension Id="ObsidianQuickNoteWidgetProvider">` is a stable string identifier (not a GUID) and is not part of the CLSID sync set — no issue.

## 2. Widget Definition IDs — ✅ CLEAN

| Manifest `<Definition Id>` | Constant | Dispatch |
|---|---|---|
| `ObsidianQuickNote` (`Package.appxmanifest:69`) | `WidgetIdentifiers.QuickNoteWidgetId` (`WidgetIdentifiers.cs:11`) | falls through to the generic `CardTemplates.LoadForSize` branch in `ObsidianWidgetProvider.PushUpdate` (`Providers/ObsidianWidgetProvider.cs:314`) |
| `ObsidianRecentNotes` (`Package.appxmanifest:90`) | `WidgetIdentifiers.RecentNotesWidgetId` (`WidgetIdentifiers.cs:12`) | matched explicitly at `Providers/ObsidianWidgetProvider.cs:308` |

Dispatch uses `StringComparison.Ordinal` (case-sensitive). Manifest casing matches the constant casing, so correct. Worth preserving.

## 3. STA + native message pump — ✅ CLEAN

- `[STAThread]` on `Main` (`Program.cs:15`). CLR auto-initializes COM apartment as STA.
- Native Win32 pump (`GetMessageW` / `TranslateMessage` / `DispatchMessageW`) intact at `Program.cs:82–88`. No managed-wait substitution (`ManualResetEvent`, `Task.Delay`, `Thread.Sleep`, `Application.Run`) — good.
- Graceful quit via `PostThreadMessageW(..., WM_QUIT, ...)` hooked to both `ProcessExit` and `Console.CancelKeyPress` (`Program.cs:75–80`). Correct.
- `GetMessageW` return-value handling at `Program.cs:85` (`ret <= 0 break`) correctly collapses WM_QUIT (0) and error (-1). Signed `int` marshalling on a `BOOL`-typed API is intentionally needed for the -1 branch — correct.

## 4. `GetCurrentThreadId` DLL — ✅ CLEAN

`Com/Ole32.cs:56–57`: `[LibraryImport("kernel32.dll")] public static partial uint GetCurrentThreadId();` — correct library, correct return type.

## 5. `ClassFactory.CreateInstance` — ✅ CLEAN

- Uses `WinRT.MarshalInspectable<IWidgetProvider>.FromManaged(_instance)` (`Com/ClassFactory.cs:50`), not `Marshal.GetIUnknownForObject`. This is the one piece of WinRT interop plumbing that was most likely to rot; it's correct.
- QI gates: accepts `IID_IWidgetProvider` and `IID_IUnknown`, returns `E_NOINTERFACE` otherwise (`Com/ClassFactory.cs:48–54`). Correct.
- Aggregation rejected with `CLASS_E_NOAGGREGATION` (`Com/ClassFactory.cs:46`). Correct.
- `LockServer` returns S_OK unconditionally (`Com/ClassFactory.cs:57`). Acceptable for an STA singleton out-of-proc server whose lifetime is managed by Widget Host.

## 6. HRESULT discipline on Co* — ✅ CLEAN

Every Co* return is inspected and logged:

- `CoRegisterClassObject` → checked, `log.Error`, return hr (`Program.cs:45–56`).
- `CoResumeClassObjects` → checked, `log.Error`, attempts revoke then returns hr (`Program.cs:58–65`).
- `CoRevokeClassObject` (both the error path and the shutdown path) → checked, `log.Warn` on failure (`Program.cs:62–63`, `90–91`). Using `< 0` to detect failure is correct (S_FALSE is defined but not used here).

No silent swallow.

## 7. `Com/Ole32.cs` P/Invoke conversion (post lint-polisher) — ✅ CLEAN

- `CoRegisterClassObject` correctly **retained as `DllImport`** with an explicit `SYSLIB1054` suppression and a comment explaining why (`Ole32.cs:12–22`). `[MarshalAs(UnmanagedType.IUnknown)] object` parameters are not supported by the `LibraryImport` source generator — keeping `DllImport` here was the correct call.
- All other P/Invokes migrated to `LibraryImport`. Signatures reviewed:
  - `CoRevokeClassObject`, `CoResumeClassObjects` — blittable, correct.
  - `GetMessageW` returns `int` (required for -1 error branch), `MSG` out is blittable — correct.
  - `TranslateMessage`, `PostThreadMessageW` — `[return: MarshalAs(UnmanagedType.Bool)]` preserved; compatible with `LibraryImport`.
  - `DispatchMessageW` — `ref MSG` + `IntPtr` return, blittable, correct.
  - `PostQuitMessage` — present but unused; harmless.
  - `GetCurrentThreadId` — `kernel32.dll`, returns `uint`.
- `MSG` struct (`Ole32.cs:30–41`) layout is sequential and fully blittable — safe under source-generated marshalling.

No regressions from the `LibraryImport` sweep.

## 8. `ObsidianWidgetProvider` attributes — ✅ CLEAN

- `partial` modifier present (`Providers/ObsidianWidgetProvider.cs:19`) — required for CsWinRT1028.
- `[ComVisible(true)]` + `[Guid(WidgetIdentifiers.ProviderClsid)]` present.
- Implements both `IWidgetProvider` and `IWidgetProvider2`. `OnCustomizationRequested` is stubbed but logged — acceptable.

---

## Findings

### HIGH

**H1. `IsComServerMode` is dead code; any non-widget launch will still register the COM server.**
`src/ObsidianQuickNoteWidget/Program.cs:102–112` — both branches fall through to `return true`. The explicit arg checks for `-RegisterProcessAsComServer` do nothing. If the exe is ever started without the widget-host arg (double-click, F5 debug, diagnostic script), it will call `CoRegisterClassObject` and then hang forever in `GetMessageW`, with no way for the caller to know. Either:
 - remove the fake gate and remove the misleading comment, **or**
 - actually enforce the gate (`return false` for the default case) and exit with a friendly message.
Current state is worse than either option because it pretends to gate.

### MED

**M1. `_folderRefreshTimer` can race with `DeleteWidget` and resurrect state for a just-deleted widget.**
`src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:261–283`. `RefreshAllActiveAsync` snapshots `_active.Keys.ToArray()` and then `await`s the CLI call. If `DeleteWidget` (`:65–70`) removes a widget during that await, the subsequent `_store.Save(state)` on line 275 will re-create the JSON state file for a widget the host just told us to forget. Post-await, check each id with `_active.ContainsKey(id)` before touching the store, or tolerate it if the store is idempotent on next launch. The single-instance timer is tolerable for dev but should be revisited before shipping — consider stopping the timer from `DeleteWidget` when `_active.IsEmpty` and restarting from `CreateWidget`.

**M2. No explicit `RoInitialize` / `CoInitializeEx`.**
`Program.cs` relies entirely on `[STAThread]` + CsWinRT-side initialization. This works today because the `MarshalInspectable<T>.FromManaged` path inside `CreateInstance` drives WinRT module init lazily, and the CLR STA-init covers COM apartment state. It is fragile: a future change that touches WinRT on the pump thread *before* any activation (e.g., logging that calls into Windows.Storage) could fail with `CO_E_NOTINITIALIZED`. Recommend an explicit `RoInitialize(RO_INIT_SINGLETHREADED)` immediately after entering `Main`, HRESULT-checked and logged.

**M3. `Program.Main` never disposes `provider` / `factory`.**
Process exit reclaims everything, but if `_folderRefreshTimer` is ever changed to a non-GC-rooted owner, timer callbacks could fire after `CoRevokeClassObject`. Low risk today because the provider is rooted by `factory` which is rooted by the COM runtime until revoke, but worth a code-comment pinning the invariant.

### LOW

**L1. `CoRegisterClassObject` declared with `ref Guid rclsid`.** Semantically `REFCLSID` is `in`. Works fine under classic `DllImport`, but `in Guid` would be more idiomatic and prevents accidental mutation by the callee (none happens, but it documents intent). `Com/Ole32.cs:17`.

**L2. `TranslateMessage` / `DispatchMessageW` take `ref MSG`.** Neither mutates the struct meaningfully post-`GetMessageW`; `in MSG` is more honest. `Com/Ole32.cs:48, 51`. Cosmetic.

**L3. `IsComServerMode` OR branch swapped** — the method uses `Equals(..., OrdinalIgnoreCase)` per arg but then unconditionally returns `true`. Redundant if H1 is accepted.

**L4. `PostQuitMessage` P/Invoke declared but never called.** `Com/Ole32.cs:53–54`. Dead code; either wire it up (send from inside `DeleteWidget` when `_active.IsEmpty`?) or remove.

**L5. `MarshalInspectable<T>.FromManaged` leaks a ref count on the error path?** Not a real concern here because the caller (COM runtime) takes ownership of the returned `IntPtr`. Pure note for future readers.

---

## Verdict

**Plumbing health: HEALTHY.** All four CLSID sync points match, Definition IDs align with dispatch, STA + native pump is intact, HRESULTs are checked, `GetCurrentThreadId` resolves from `kernel32`, and the `LibraryImport` conversion was done correctly — critically keeping `CoRegisterClassObject` on `DllImport` because of the `[MarshalAs(IUnknown)] object` parameter. `WinRT.MarshalInspectable<IWidgetProvider>.FromManaged` is still the factory output — no classic-CCW regression.

### Top 3 issues (by risk × likelihood)

1. **[HIGH] H1 — `IsComServerMode` always returns `true`.** Any stray launch (debugger, diagnostic) silently boots a COM server that then hangs in the message pump. Fix or delete the gate.
2. **[MED] M1 — Timer vs `DeleteWidget` race can resurrect a deleted widget's JSON state.** Snapshot-then-await in `RefreshAllActiveAsync` is the culprit.
3. **[MED] M2 — No explicit `RoInitialize`.** Works today by accident-of-ordering; add an explicit, HRESULT-checked call at the top of `Main` to harden against future interop changes.

No CRITICAL findings — activation path is sound.
