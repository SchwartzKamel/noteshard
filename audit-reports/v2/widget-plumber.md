# widget-plumber audit — v2

**Scope:** verify v1 fixes landed without regressing the COM/WinRT/STA-pump plumbing.
**Mode:** READ-ONLY. No source files modified.

---

## 1. v1 follow-ups — verification

### H1 (v1) — `IsComServerMode` gate — ✅ FIXED

`src/ObsidianQuickNoteWidget/Program.cs:104–116`. The method now returns `true`
**only** when `args` contains `-Embedding` or `/Embedding` (case-insensitive),
otherwise `false`. The `Main` non-embedding branch (`Program.cs:35–41`) writes
`"Not launched as COM server. Exiting."` to the log, prints a friendly message
to the console (best-effort, swallowed if no console), and `return 0`s before
ever calling `CoRegisterClassObject`. Cannot accidentally hang from a
double-click / debug F5 launch any more. Clean.

Note: the historical Widget Host arg `-RegisterProcessAsComServer` is no longer
recognised. That is consistent with the comment ("Widget Host launches us with
`-Embedding`"), but if the host ever passes a different sentinel we'll silently
print + exit. Acceptable — the proof-of-life log at `Program.cs:20–28` always
fires unconditionally, so misclassification will be obvious from
`%USERPROFILE%\ObsidianWidget-proof.log`.

### M1 (v1) — Timer vs `DeleteWidget` race — ✅ FIXED

`src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs`:

- `DeleteWidget` (`:80–92`) wraps `_active.TryRemove` + `_store.Delete` in
  `_gate.WithLockAsync(widgetId, …)`. Any in-flight refresh/action either
  finished before delete enters the gate, or starts after delete exits — and
  in the latter case finds `_active.ContainsKey == false` and bails.
- `RefreshAllActiveAsync` (`:359–380`) snapshots `_active.Keys.ToArray()`,
  awaits the (gate-free) CLI call, then for each id re-enters the per-id gate
  and re-checks `if (!_active.ContainsKey(id)) return Task.CompletedTask;`
  before touching the store. Save is skipped for deleted widgets — no
  resurrection.
- Same re-check pattern is consistently applied in `RefreshFolderCacheAsync`
  (`:344`), `OnWidgetContextChanged` (`:118`), `CreateNoteAsync` (`:247`),
  `pasteClipboard` handler (`:205`), and the error path inside `FireAndLog`
  (`:172`). The invariant — *"never write to `_store` for an id not in
  `_active`, except in `CreateWidget` itself"* — holds.

Race closed.

---

## 2. Plumbing invariants from the archetype guardrails

| Invariant | Location | State |
|---|---|---|
| `[STAThread]` on `Main` | `Program.cs:15` | ✅ intact |
| Native pump `GetMessageW` / `TranslateMessage` / `DispatchMessageW` (no managed wait substitution) | `Program.cs:84–90`, `Com/Ole32.cs:43–51` | ✅ intact |
| `MarshalInspectable<IWidgetProvider>.FromManaged(_instance)` returned from `IClassFactory.CreateInstance` (NOT `Marshal.GetIUnknownForObject`) | `Com/ClassFactory.cs:50` | ✅ intact |
| `CoRegisterClassObject` declared with `[DllImport]` + `#pragma warning disable SYSLIB1054` (because of `[MarshalAs(UnmanagedType.IUnknown)] object` param) — **not** regressed to `[LibraryImport]` | `Com/Ole32.cs:14–22` | ✅ intact, comment preserved |
| `GetCurrentThreadId` resolved from `kernel32.dll` | `Com/Ole32.cs:56–57` | ✅ intact |
| `partial` modifier on `ObsidianWidgetProvider` | `Providers/ObsidianWidgetProvider.cs:20` | ✅ intact |
| HRESULT checked + logged on `CoRegisterClassObject` / `CoResumeClassObjects` / `CoRevokeClassObject` | `Program.cs:54–66, 92–93` | ✅ intact |
| `WM_QUIT` posted via `PostThreadMessageW` from `ProcessExit` + `Console.CancelKeyPress` | `Program.cs:74–82` | ✅ intact |
| Provider CLSID sync (4 points) | unchanged since v1 | ✅ still in sync (no GUID-touching diff in this round) |

No regressions from any plumbing guardrail.

---

## 3. Fresh checks — `FireAndLog` / `AsyncSafe` interaction with the STA pump

The new fan-out idiom is: every COM callback (`CreateWidget`, `DeleteWidget`,
`OnActionInvoked`, `OnWidgetContextChanged`, `Activate`) calls `FireAndLog(...)`
and discards the returned `Task`. The COM callback then returns immediately on
the STA pump thread.

Walked the call paths to confirm no managed wait sneaks back onto the pump:

- `FireAndLog` (`ObsidianWidgetProvider.cs:160–186`) → `AsyncSafe.RunAsync`
  (`Concurrency/AsyncSafe.cs:16–36`). Both bodies are `async`, both `await` with
  `.ConfigureAwait(false)`. The first `await` releases the STA pump thread
  back to `GetMessageW`. ✅
- The COM callback methods themselves do `FireAndLog(...)` then return `void`;
  they never `.Wait()`, `.Result`, or `.GetAwaiter().GetResult()`. ✅
- `FireAndLog`'s `ContinueWith(_ => SafePushUpdate(...), TaskScheduler.Default)`
  (`:182–185`) explicitly forces the continuation onto the thread-pool, away
  from the STA. `PushUpdate` is therefore called from the pool, never blocking
  the pump. ✅
- The Timer callback (`:51–55`) lives on the threadpool and does not touch the
  STA. ✅
- `AsyncKeyedLock<TKey>.WithLockAsync` uses `SemaphoreSlim.WaitAsync` — no
  blocking wait. ✅

**No new managed wait was introduced on the pump thread.** The `MoAppHang`
exposure surface is unchanged.

### Notes worth flagging (not regressions)

- **Sync-over-async on `Clipboard.GetTextAsync`.** `TryReadClipboardText`
  (`ObsidianWidgetProvider.cs:455–469`) calls
  `op.AsTask().GetAwaiter().GetResult()`. This runs from the threadpool (after
  `FireAndLog → AsyncSafe.RunAsync`'s first await), **not** the STA pump, so
  the pump is safe. WinRT `Clipboard.GetContent` historically prefers an STA
  context, however; under thread-pool execution this can occasionally throw
  `RPC_E_WRONG_THREAD` and end up in the `catch { }` returning `null`. That is
  a UX nit, not a plumbing bug, and it never reaches the pump.
- **`PushUpdate` calls `WidgetManager.GetDefault().UpdateWidget(...)` from
  the threadpool.** `WidgetManager` is a WinRT proxy and will marshal the call
  itself; safe. Just noting the pattern for future readers.

### RoInitialize ordering

Still no explicit `RoInitialize` / `CoInitializeEx` in `Program.Main`.
`[STAThread]` covers COM apartment init, and CsWinRT lazily drives WinRT
module init the first time `MarshalInspectable<T>.FromManaged` runs inside
`CreateInstance`. That ordering is "by accident": **today** nothing on the
STA pump thread touches WinRT *before* the first `CreateInstance` call (the
proof-of-life write uses only `System.IO`, the FileLog uses only
`System.IO`, and `CoRegisterClassObject` / `CoResumeClassObjects` are
classic-COM). If a future change adds a WinRT call (e.g., `WidgetManager`
warm-up, `Windows.Storage` access, anything under `Microsoft.Windows.*`)
before the first activation, it will surface as `CO_E_NOTINITIALIZED` or
`RO_E_*`. Recommendation unchanged from v1: add an explicit
`RoInitialize(RO_INIT_SINGLETHREADED)` immediately after entering `Main`,
HRESULT-checked and logged.

---

## 4. New/remaining findings

### MED

**M1 (v2). `RoInitialize` still implicit.** Carry-forward from v1 M2. Today
fine, fragile under change. See §3.

### LOW

**L1 (v2). `_folderRefreshTimer` runs even when `_active.IsEmpty`.** The
timer fires every 2 minutes for the lifetime of the process; the callback
short-circuits at `if (_active.IsEmpty …) return;` (`:361`), but the wakeup
itself is wasted on an idle COM server that the host may decide to keep
around. Minor — could stop/start the timer from `CreateWidget` /
`DeleteWidget`, but not worth churn.

**L2 (v2). `IsComServerMode` flips silently on unexpected args.** A user
launching with, say, `-Help` will get the generic "this is a COM server"
message and exit 0. Fine, but consider an explicit `--help` / `--version`
branch one day. Cosmetic.

**L3 (v2). `PostQuitMessage` P/Invoke still declared, still unused.**
`Com/Ole32.cs:53–54`. Carry-over from v1. Either wire it (e.g., from inside
`DeleteWidget` when `_active.IsEmpty`, with caution around host re-pin) or
delete.

**L4 (v2). Cosmetic: `ref Guid` on `CoRegisterClassObject`, `ref MSG` on
`TranslateMessage` / `DispatchMessageW`.** Carry-over from v1 L1/L2.
Semantically `in`, no behavioural impact.

---

## 5. Verdict

**Plumbing health: HEALTHY. v1 H1 + M1 both fixed cleanly with no
regressions.** Activation path, native pump, WinRT marshalling, P/Invoke
signatures, HRESULT discipline, and CLSID/GUID sync all intact. The
fire-and-forget refactor (`FireAndLog` + `AsyncSafe.RunAsync` +
`AsyncKeyedLock`) does **not** introduce any managed wait on the STA pump
thread.

### Top 3 issues (by risk × likelihood)

1. **[MED] M1 (v2) — No explicit `RoInitialize`.** Same as v1 M2; not yet
   addressed. Add `RoInitialize(RO_INIT_SINGLETHREADED)` at the top of
   `Main` to harden ordering against future WinRT-on-pump-thread changes.
2. **[LOW] L3 (v2) — Dead `PostQuitMessage` P/Invoke.** Either use it
   (auto-shutdown on `_active.IsEmpty`) or delete it.
3. **[LOW] L1 (v2) — Folder-refresh Timer runs on an idle server.** Cheap
   to fix by stopping/starting from `Create`/`DeleteWidget`; not a
   correctness issue.

No CRITICAL findings. No HIGH findings. v1's HIGH was retired this round.
