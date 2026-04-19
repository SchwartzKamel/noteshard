# widget-plumber audit — v3

**Scope:** re-verify COM/WinRT/STA-pump plumbing and audit the
`folderNew` Adaptive-Card input round-trip through `actionData` →
`ParseInputs` → `NoteRequest.Folder`.
**Mode:** READ-ONLY. No source files modified.

---

## 1. Plumbing invariants — re-verified

| Invariant | Location | State |
|---|---|---|
| `[STAThread]` on `Main` | `Program.cs:15` | ✅ intact |
| Native pump `GetMessageW` / `TranslateMessage` / `DispatchMessageW` (no managed-wait substitution) | `Program.cs:84–90`, `Com/Ole32.cs:43–51` | ✅ intact |
| `WinRT.MarshalInspectable<IWidgetProvider>.FromManaged(_instance)` returned from `IClassFactory.CreateInstance` (NOT `Marshal.GetIUnknownForObject`) | `Com/ClassFactory.cs:50` | ✅ intact |
| `IsComServerMode` requires `-Embedding` / `/Embedding`; default branch returns `false` and exits cleanly | `Program.cs:35–41, 104–116` | ✅ intact (v1-H1 fix preserved) |
| `CoRegisterClassObject` declared with `[DllImport]` + `#pragma SYSLIB1054` (because of `[MarshalAs(UnmanagedType.IUnknown)] object` param) — **not** regressed to `[LibraryImport]` | `Com/Ole32.cs:14–22` | ✅ intact, comment + suppression preserved |
| `GetCurrentThreadId` resolved from `kernel32.dll` | `Com/Ole32.cs:56–57` | ✅ intact |
| `partial` modifier on `ObsidianWidgetProvider` (CsWinRT1028) | `Providers/ObsidianWidgetProvider.cs:20` | ✅ intact |
| HRESULT checked + logged on `CoRegisterClassObject` / `CoResumeClassObjects` / `CoRevokeClassObject` | `Program.cs:54–67, 92–93` | ✅ intact |
| `AsyncKeyedLock<TKey>.WithLockAsync` gates every `Get → mutate → Save` on the store | `Providers/ObsidianWidgetProvider.cs:33, 67, 86, 245, …`, `Core/Concurrency/AsyncKeyedLock.cs` | ✅ intact, `SemaphoreSlim.WaitAsync` only — no blocking wait on the STA pump |
| Provider CLSID sync (4 points, `B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91`) | `WidgetIdentifiers.cs:8`, `Providers/ObsidianWidgetProvider.cs:19`, `Package.appxmanifest:47, 66` | ✅ in sync |

No regressions from v2.

---

## 2. `folderNew` round-trip — fresh audit

**Path under test:** Adaptive-Card `Input.Text id="folderNew"` → host
serialises submit values to a JSON object → `IWidgetProvider.OnActionInvoked`
delivers it as `WidgetActionInvokedArgs.Data` (a `string`) → in this repo
that string is the `actionData` parameter to
`CreateNoteAsync(WidgetSession, string?)`
(`Providers/ObsidianWidgetProvider.cs:238–280`) → parsed by
`ParseInputs(string?)` (`:433–453`) → looked up as
`inputs.GetValueOrDefault("folderNew")?.Trim()` (`:251`) → flows into
`NoteRequest.Folder` (`:270–278`) → consumed by `NoteCreationService` /
`ObsidianCli`.

### 2.1 Decoding — ✅ CLEAN

`ParseInputs` uses
`System.Text.Json.JsonDocument.Parse(json)` + `prop.Value.GetString()`.
`GetString()` returns the canonical, fully-unescaped .NET (UTF-16) string
for any `JsonValueKind.String`. Concretely:

| Typed input | On-the-wire JSON (host-emitted) | After `GetString()` |
|---|---|---|
| `Notes/Inbox` | `"Notes/Inbox"` | `Notes/Inbox` |
| `Notes\Inbox` | `"Notes\\Inbox"` | `Notes\Inbox` |
| `日本語/メモ` | `"日本語/メモ"` *or* `"\u65E5\u672C\u8A9E/\u30E1\u30E2"` | `日本語/メモ` |
| `Inbox 😀` (U+1F600) | `"Inbox \uD83D\uDE00"` | `Inbox 😀` (correct surrogate-pair decode) |
| `Line1\r\nLine2` | `"Line1\r\nLine2"` | `Line1\r\nLine2` (CRLF preserved through parse — `Trim()` then strips it, see §2.3) |
| `path with "quote"` | `"path with \"quote\""` | `path with "quote"` |
| `tab\there` | `"tab\there"` | `tab\there` (then `Trim()` leaves the embedded tab alone — only outer whitespace stripped) |

There is **no** double-decoding, no `Encoding.GetBytes(string)` UTF-8
re-round-trip, no `Uri.UnescapeDataString` — just the JSON parser, which
is the right tool. Bytes-vs-string overload of `JsonDocument.Parse` is
the `string` one (implicit `ReadOnlyMemory<char>`), so there is no UTF-8
round-trip and no risk of `EncoderFallbackException` on lone surrogates.

### 2.2 Dictionary key collisions — note (not a bug today)

`ParseInputs` uses `StringComparer.OrdinalIgnoreCase`
(`:435`). The Adaptive Card emits a single key `folderNew`; if the
template ever introduces a sibling `Folder`, `FOLDER`, `folderNEW`, etc.,
the last one wins (JSON enumeration order is preserved, so it's the
JSON-source order). Worth a code-comment if more inputs are added.

### 2.3 `Trim()` semantics — note

`folderNew` is `?.Trim()` (`:251`), default `Trim()` strips Unicode
white-space per `char.IsWhiteSpace` — including CR, LF, NBSP (U+00A0),
and the various Unicode spaces. Emoji, ZWJ (U+200D), and BOM-in-the-
middle are **not** stripped. Behaviour matches the documented "type a
folder path" UX. The interior of the string is untouched, so embedded
`\n` survives to the filesystem layer (where the OS will then reject
it). That's not a plumbing concern.

### 2.4 Failure modes (defensive, not bugs)

- **Malformed JSON / non-object root.** `try { ... } catch { /* ignore
  malformed */ }` returns an empty `Dictionary` (`:451`). `folderNew`
  becomes `null` → falls back to `state.LastFolder`. If the host ever
  sends a non-JSON `Data` payload (e.g., a future Card schema or an
  Action.Submit with a primitive `data:` value), the user's typed folder
  is silently lost. Consider logging at `Debug` level on parse failure
  so we at least see the swallow in `log.txt`.
- **Embedded NUL (`\u0000`).** Decoded fine by `JsonDocument`; will
  later throw `ArgumentException` at the file-system boundary. Caught
  by `NoteCreationService`, surfaces as a failed `result.Status`.
- **Path traversal (`..\..\Windows\System32`).** Not validated here;
  this is a **security-auditor** concern, flagged for cross-reference.

### 2.5 Verdict on §2

`folderNew` round-trips losslessly for `/`, `\`, multi-byte UTF-8,
emoji surrogate pairs, embedded quotes, tabs, and CRLF-in-the-middle.
The COM/JSON seam is not the layer at which any of these would be
mangled.

---

## 3. Carry-over findings

### MED

**M1 (v3, ← v2 M1, ← v1 M2). `RoInitialize` still implicit.**
Still no explicit `RoInitialize(RO_INIT_SINGLETHREADED)` at the top of
`Main`. Works today because nothing on the STA pump thread touches WinRT
*before* the first `IClassFactory.CreateInstance` (proof-of-life and
`FileLog` are pure `System.IO`; `CoRegisterClassObject` /
`CoResumeClassObjects` are classic-COM). Any future warm-up that pokes a
`Microsoft.Windows.*` or `Windows.*` API on the pump thread before
activation will surface as `CO_E_NOTINITIALIZED`. Recommendation
unchanged: add an explicit, HRESULT-checked `RoInitialize` call.

### LOW

**L1 (v3, ← v2 L1). `_folderRefreshTimer` runs even when `_active.IsEmpty`.**
Fires every 2 minutes for the lifetime of the process; callback
short-circuits (`:361`). Cheap to fix by stop/start in
`CreateWidget` / `DeleteWidget`. Not a correctness issue.

**L2 (v3, ← v2 L2). `IsComServerMode` flips silently on unexpected args.**
`-Help`, `--version`, `/?` etc. all hit the generic
"this is a Widgets COM server" message and `return 0`. Cosmetic.

**L3 (v3, ← v2 L3, ← v1 L4). `PostQuitMessage` P/Invoke declared, never called.**
`Com/Ole32.cs:53–54`. Either wire it (e.g., self-shutdown after
`DeleteWidget` when `_active.IsEmpty`, with care around host re-pin) or
delete the dead declaration.

**L4 (v3, ← v2 L4, ← v1 L1/L2). Cosmetic: `ref Guid` on
`CoRegisterClassObject`, `ref MSG` on `TranslateMessage` /
`DispatchMessageW`.** Semantically `in`. No behavioural impact.

### NEW LOW

**L5 (v3). `ParseInputs` swallows JSON parse errors silently.**
`Providers/ObsidianWidgetProvider.cs:451`: `catch { /* ignore malformed */ }`.
If the Widget Host ever emits a non-JSON `Data` payload (or our card
template starts emitting one — e.g., an `Action.Execute` with a
primitive `data` value), every typed input — including `folderNew` —
silently becomes empty and the create falls back to `state.LastFolder`.
The user sees their note land in the previous folder with no log line.
Cheap fix: log at `Warn` with the first 200 chars of the offending
payload so the failure is observable in `log.txt`.

### NEW LOW

**L6 (v3). `WidgetActionInvokedArgs.Data` is treated as nullable but not
length-bounded.** `ParseInputs` will happily try to parse a multi-MB
string. The Widget Host caps card payloads, so this is theoretical, but
a `json.Length > 64 * 1024 → log + bail` guard would make a malicious /
buggy host non-fatal. Defensive only.

---

## 4. Verdict

**Plumbing health: HEALTHY.** All v2 invariants intact: STA + native
pump, `MarshalInspectable<IWidgetProvider>.FromManaged`,
`IsComServerMode -Embedding` gate, `CoRegisterClassObject` retained as
`DllImport`, `GetCurrentThreadId` from `kernel32`, `AsyncKeyedLock`
gating every store mutation. No regression.

The `folderNew` data path is **clean end-to-end**: forward slashes,
backslashes, CJK, emoji (surrogate pairs), embedded quotes, tabs, and
CRLF-in-the-middle all survive `JsonDocument.Parse` →
`JsonElement.GetString()` losslessly. The only data-loss vector is the
silent `catch { }` in `ParseInputs` (L5).

### Top 3 issues (by risk × likelihood)

1. **[MED] M1 (v3) — No explicit `RoInitialize`.** Carried since v1.
   Add `RoInitialize(RO_INIT_SINGLETHREADED)` at the top of `Main` to
   harden ordering against future WinRT-on-pump-thread changes.
2. **[LOW] L5 (v3) — `ParseInputs` swallows JSON errors silently.**
   Log at `Warn` so a host-format change does not silently drop every
   typed input (including `folderNew`).
3. **[LOW] L3 (v3) — Dead `PostQuitMessage` P/Invoke.** Wire it (auto-
   shutdown when `_active.IsEmpty`) or delete it; carry-over since v1.

No CRITICAL findings. No HIGH findings. v1's HIGH (`IsComServerMode`)
remains retired.
