# Bug-Hunter Sweep v3 — ObsidianQuickNoteWidget

Read-only follow-up audit. **No source modified.** Verifies v1/v2 findings against
HEAD and surfaces fresh bugs introduced by commit **`750f032`** ("Restore compact
folder dropdown + add separate `folderNew` text input").

Repo baseline: HEAD @ `750f032`. Recent churn since v2:

```
750f032  Restore compact folder dropdown + add separate 'new folder' text input  (THIS SWEEP)
d2cb0f6  Security hardening: F-01/F-02/F-03                                       (v2 baseline)
f12a196  Initial commit                                                            (v1 baseline)
```

`750f032` touches exactly the surface this sweep was scoped to:

| File                                                                                | ±lines |
|-------------------------------------------------------------------------------------|--------|
| `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs` (`CreateNoteAsync`) | +4 / −1 |
| `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/QuickNote.medium.json`    | +9 / −1 |
| `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/QuickNote.large.json`     | +10 / −1 |
| `src/ObsidianQuickNoteWidget/Package.appxmanifest`                                  |  1.0.0.1 → 1.0.0.2 |
| `tests/.../CardTemplatesTests.cs`                                                   | discriminator updated |

Notably **`QuickNote.small.json` was NOT updated** (still no `folderNew` input —
small users cannot type a new folder), and `RecentNotes.json` is untouched.

Legend — Likelihood: HIGH / MED / LOW. Severity: HIGH (data loss / user-visible
failure), MED (degraded UX / hidden failure), LOW (cosmetic / very narrow).

---

## 1. Delta table — v1 → v2 → v3 status per finding

| ID  | Title                                                                  | v1 | v2                | v3                | Notes |
|-----|------------------------------------------------------------------------|----|-------------------|-------------------|-------|
| B1  | R-M-W race on `WidgetState`                                            | OPEN | FIXED (in-proc) | FIXED (in-proc) | Cross-process LWW with tray still open by design (`JsonStateStore` xmldoc 15–21). |
| B2  | Timer resurrects deleted widget rows                                   | OPEN | FIXED            | FIXED            | `RefreshAllActiveAsync` re-checks `_active.ContainsKey` under the gate (provider L375). |
| B3  | `OnActionInvoked` swallows async exceptions                            | OPEN | FIXED            | FIXED            | `FireAndLog`/`AsyncSafe.RunAsync` route faults to `WriteStatus`+`SafePushUpdate`. |
| B4  | `JsonStateStore.Persist` swallows `.tmp` / Move failures               | OPEN | OPEN             | OPEN             | `JsonStateStore.cs` outer `catch { }` unchanged. |
| B5  | `FileLog` cross-process sharing violation                              | OPEN | OPEN             | OPEN             | `File.AppendAllText` + swallow unchanged. |
| B6  | `TryReadClipboardText` sync-over-async on COM thread                   | OPEN | OPEN             | OPEN             | Provider L467 `op.AsTask().GetAwaiter().GetResult()` unchanged. |
| B7  | `ObsidianCli.RunAsync` truncates stderr on timeout                     | OPEN | OPEN             | OPEN             | Reader tasks still orphaned on `OperationCanceledException`. |
| B8  | `RunAsync` rethrows `IOException` from `StandardInput.WriteAsync`      | OPEN | OPEN             | OPEN             | No caller passes stdin; latent. |
| B9  | `DuplicateFilenameResolver` TOCTOU vs CLI create                        | OPEN | PARTIAL          | PARTIAL          | CLI auto-rename caps damage; race window unchanged. |
| B10 | `QuickNoteForm` folder-fetch Task crashes after dispose                 | OPEN | OPEN             | OPEN             | Tray untouched in this commit. |
| B11 | `QuickNoteForm.CreateAsync` async-void via lambda                       | OPEN | OPEN             | OPEN             | Tray untouched. |
| B12 | `ParseFolders` drops dot-prefixed folders                               | OPEN | OPEN (design)    | OPEN (design)    | — |
| B13 | `CardTemplates.Load` uses `EndsWith(name)`                             | OPEN | OPEN             | OPEN             | — |
| B14 | `IsReservedWindowsName` misses `CONIN$`/`CONOUT$`                      | OPEN | OPEN             | OPEN             | — |
| B15 | `FolderPathValidator` accepts UNC paths after normalize                | OPEN | OPEN             | OPEN             | **Now reachable from the widget UI via `folderNew`** — see N13/N15. |
| B16 | `CardDataBuilder` NRE risk on null list entries                        | OPEN | OPEN (reduced)   | OPEN (reduced)   | — |
| B17 | Timer never disposed                                                    | ACK  | ACK              | ACK              | — |
| N1  | `Deactivate` mutates `_active` without the gate                        | —    | NEW              | OPEN             | Provider L142 unchanged. |
| N2  | `OnActionInvoked` `_active.GetOrAdd` outside the gate (resurrection)   | —    | NEW              | OPEN             | Provider L102–106 unchanged. |
| N3  | Best-effort post-create state re-read outside gate                     | —    | NEW              | OPEN             | Provider L315–319 unchanged. |
| N4  | `FireAndLog.onError` drops error if widget no longer in `_active`      | —    | NEW              | OPEN             | Provider L172 guard unchanged. |
| N5  | `FireAndLog` double-pushes update on failure (`onError` + `ContinueWith`) | — | NEW              | OPEN             | Provider L181 + L184 unchanged. |
| N6  | `BuildCliMissingData` called without `widgetId`                        | —    | NEW              | OPEN             | Provider L398 still passes only the detail. |
| N7  | `${widgetId}` vs `${$root.widgetId}` template inconsistency            | —    | NEW              | OPEN             | New template additions in 750f032 only touch `body`, not `actions` — consistency unchanged. |
| N8  | Tray 600 ms `Hide()` race                                              | —    | NEW              | OPEN             | Tray untouched. |
| N9  | `ObsidianCli` orphaned reader tasks on timeout                         | —    | NEW              | OPEN             | — |
| N10 | `CliResult.Succeeded` semantic landmine                                | —    | NEW              | OPEN             | — |
| N11 | `RefreshAllActiveAsync` per-id serial loop                             | —    | NEW              | OPEN             | — |
| N12 | `state.LastFolder` persisted from unvalidated user input               | —    | NEW              | **WORSE**        | Free-form typing is now the documented path → see N13. |

### Verification summary

- **B1/B2/B3 hardening still holds.** `AsyncKeyedLock` is still ref-counted with
  disposal under `SyncRoot` and lost-race retry (`AsyncKeyedLock.cs` 73–115). I
  re-walked the lock-acquisition / cancellation paths and could not construct a
  self-deadlock, double-dispose, or missed-release. `AsyncSafe.RunAsync` still
  catches the `onError` handler's own throws (`AsyncSafe.cs:33`).
- **No prior finding regressed.** All v2 OPEN items are byte-identical at HEAD.
- The new commit's surface is intentionally narrow (one parsing branch + two
  templates), but every new bug below stems from that same surface.

---

## 2. New bugs introduced by commit `750f032`

### N13. `inputs.folderNew` is **not** populated in card data → template binding resolves to nothing every render
**Files:**
- `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/QuickNote.medium.json:30–34`
- `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/QuickNote.large.json` (mirror)
- `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs:22–32`

**What's wrong.** The new template input declares
```json
{ "type": "Input.Text", "id": "folderNew",
  "placeholder": "…or type new folder (optional)",
  "value": "${$root.inputs.folderNew}" }
```
but `CardDataBuilder.BuildQuickNoteData` never adds a `folderNew` key under
`inputs`. The Adaptive Cards templater resolves `${$root.inputs.folderNew}` to
*undefined* and drops the `value` property — so on every `PushUpdate` the field
re-renders empty. There is no `state.LastFolderNew` either.

**Symptoms:**
1. **Lost in-progress typing on host re-render.** When the 2-minute folder-cache
   timer (`RefreshAllActiveAsync`) fires `pushUpdateOnCompletion: true` while the
   user is typing into the new-folder field, the host re-applies the template and
   the partially-typed value is wiped. Title/body have the same exposure but at
   least their bindings *exist* — `folderNew` is unique in that the binding
   resolves to undefined regardless of state.
2. **Cosmetic risk on stricter Adaptive Cards renderers.** Some renderers
   (depending on host version) leave the literal `${$root.inputs.folderNew}` text
   visible in the field instead of dropping the property, which would be
   embarrassing in production.

**Likelihood:** HIGH (every refresh tick during typing — guaranteed every 2 min).
**Severity:** MED (silent loss of typed input).

**Suggested fix shape.** Add `["folderNew"] = string.Empty` to the
`["inputs"]` JsonObject in `BuildQuickNoteData` (it's intentionally always cleared
post-submit; explicitly writing `""` is the simplest correct binding).

**Regression test.** `CardDataBuilderTests`: assert
`JsonNode.Parse(BuildQuickNoteData(...))!["inputs"]!["folderNew"]` is non-null.
Today it is `null`, which would have caught this immediately.

---

### N14. `state.LastFolder` set to `folderNew` is invisible in the dropdown — silently reverts to vault root on next note
**File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:251–254, 282`

**What's wrong.** When the user types a brand-new folder into `folderNew` and
submits successfully:
```csharp
var folderNew = inputs.GetValueOrDefault("folderNew")?.Trim();
var folder = !string.IsNullOrEmpty(folderNew) ? folderNew : ...;
...
state.LastFolder = folder ?? string.Empty;     // = the just-typed new folder
```
`folder` is now persisted as `state.LastFolder`, and on next render the dropdown
binds `value: ${$root.inputs.folder}` = `state.LastFolder`. **But the new folder
isn't in `folderChoices` yet** — the auto-refresh fires *after* the create
returns, races the next render, and the Obsidian CLI itself only sees the new
folder once a note has actually been written into it (the create *does* create
the folder, so this part eventually converges, but not before the immediate
re-render).

When the dropdown's `value` doesn't match any `choice`, Adaptive Cards
`Input.ChoiceSet` (compact / dropdown style) shows nothing selected and emits
`""` at submit time. Since `folderNew` is also empty (per N13), the next
`createNote` resolves:
```
folder = !empty(folderNew) ? folderNew : (inputs.folder ?? state.LastFolder)
       = (inputs.folder = "")              // key present, value empty → wins over LastFolder
```
**The user's "remembered" folder is silently dropped** — the next note lands in
the vault root, contradicting the visible "Created in `MyFolder/`" status from
the previous round.

**Likelihood:** HIGH (any user who creates two consecutive notes in a freshly
typed folder, before the cache catches up).
**Severity:** MED-HIGH (silent wrong-folder placement of a note — recoverable
by the user but only if they notice).

**Suggested fix shape.** Either (a) push the freshly typed folder into
`state.CachedFolders` (or `state.RecentFolders`, which `BuildFolderChoices`
already prepends with 🕑) inside the success branch so it is in the dropdown on
the very next render, or (b) fall back to `state.LastFolder` when
`inputs.folder` is empty *and* no `folderNew` was submitted.

**Regression test.** Provider integration test: with a fake CLI whose
`ListFoldersAsync` returns `["A", "B"]`, submit `createNote` with
`folderNew = "NewFolder"`. Assert that the next `BuildQuickNoteData` payload's
`folderChoices` contains `"NewFolder"`. Today: it doesn't.

---

### N15. `folderNew` bypasses no validator before being persisted to disk (compounds N12 + B15)
**File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:251–282`

**What's wrong.** `NoteCreationService.CreateAsync` calls `FolderPathValidator`
before invoking the CLI, so a bad `folderNew` (e.g. `../escape`, `C:\abs`,
`\\unc\share\foo` which slips B15, or a segment like `CON`) is rejected with
`NoteCreationStatus.InvalidFolder`. **But the provider has already committed to
`state.LastFolder = folder` before checking `result.Status`** (the v2 N12 bug,
unchanged). With `folderNew`, the user can now type *anything* and the rejected
value is what gets persisted — and per N14 it isn't visible in the dropdown for
correction.

Concretely, an inadvertent leading-slash path (`/inbox`) or `..\foo` typed once
becomes the sticky `LastFolder` forever. The rejection toast disappears on the
next interaction; the bad path is invisible (dropdown can't show it, `folderNew`
field is cleared per N13), but the next render's dropdown `value` is still the
rejected path → Input.ChoiceSet emits `""` (per N14) → user *thinks* they're
saving to vault root, error toast vanished, all is well — except `state.json`
on disk now contains a path that fails every audit.

**Likelihood:** MED (any typo in `folderNew` triggers the cascade).
**Severity:** MED (persistent bad-state in `state.json`; user-confusing).

**Suggested fix shape.** Same as v2 N12: move the
`state.LastFolder = folder ?? string.Empty;` assignment **inside** the
`result.Status == NoteCreationStatus.Created` branch (mirroring the existing
`state.LastCreatedPath` placement). Optional defense-in-depth: also call
`FolderPathValidator.TryValidate(folderNew, ...)` before letting it shadow the
picker, surfacing `state.LastError` directly without round-tripping through the
CLI.

**Regression test.** Provider test: stub `IObsidianCli` so `CreateNoteAsync`
returns `NoteCreationStatus.InvalidFolder`; submit with
`folderNew = "../escape"`; assert `_store.Get(id).LastFolder` is unchanged.
Today the stored value becomes `"../escape"`.

---

### N16. `folderNew` precedence ignores the picker even when the picker was explicitly changed
**File:** `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:251–254`

**What's wrong.** Precedence is "any non-empty `folderNew` wins." Because
`folderNew` is *not* cleared by the data binding (N13: it's just always
re-resolved to undefined → empty on the *next* render), a single in-flight
sequence is fine. But there is no UI affordance making the precedence visible to
the user — the picker still looks active. A user who:
1. types `Inbox` into `folderNew`, submits (works);
2. notices `Inbox` is now in the dropdown via the auto-refresh (N14
   notwithstanding, it eventually appears);
3. picks `Archive` from the dropdown to write the next note;
4. forgets the new-folder input, doesn't realize their old text is still there
   (it isn't — the field rendered empty per N13 — but if a future fix corrects
   N13 by echoing `state.LastFolder`, **this becomes a real foot-gun**).

This is conditional on N13's fix — calling it out so the obvious-looking "echo
LastFolder into folderNew" repair to N13 *doesn't* silently introduce a worse
bug. Today (per N13) the field is always empty, so this is latent.

**Likelihood:** LOW today; HIGH if N13 is fixed naively.
**Severity:** MED (wrong-folder writes that look correct in the picker).

**Suggested fix shape.** When fixing N13, bind `folderNew` to
`string.Empty` always (never to `state.LastFolder` or any persisted "last typed"
value) and add a hint to the placeholder, OR clear `folderNew` on the
client side via an `Action.Submit`-style reset (Adaptive Cards 1.5 has no clean
way to do this — best to keep the data binding always empty).

**Regression test.** End-to-end provider test sequence (1)→(2)→(3); assert
that when `folderNew` is empty in the inputs, the picker value wins, regardless
of any persisted `state.LastFolder`.

---

### N17. Manifest version bump to `1.0.0.2` is not reflected in `CHANGELOG.md`'s "Unreleased"
**Files:** `src/ObsidianQuickNoteWidget/Package.appxmanifest`, `CHANGELOG.md`

**What's wrong.** `Package.appxmanifest` advanced from `1.0.0.1` → `1.0.0.2` in
this commit, but the `CHANGELOG.md` "Unreleased" section was not updated to
mention either the dropdown revert or the `folderNew` input. A
release-engineer who runs `make release` against HEAD would ship a binary whose
in-product version diverges from the changelog narrative. (Strictly a
release-hygiene bug, but in scope for "new bugs introduced by 750f032".)

**Likelihood:** LOW (caught at release-prep time). **Severity:** LOW.

---

### Re-verified, *unchanged* by 750f032 (still OPEN)

- **AsyncKeyedLock semantics around `_active.ContainsKey`.** The pattern
  ```csharp
  await _gate.WithLockAsync(id, async () => {
      if (!_active.ContainsKey(id)) return;
      var s = _store.Get(id); ...; _store.Save(s);
  });
  ```
  is still correct *within* the in-process model: `DeleteWidget` mutates
  `_active` under the same per-id gate, so the guard either sees the entry
  (delete hasn't started) or doesn't (delete completed). The two doors that
  bypass the gate — `Deactivate` (N1) and `OnActionInvoked.GetOrAdd` (N2) — are
  unchanged from v2. **No new gate-related bug** in 750f032; the new
  `CreateNoteAsync` precedence logic runs entirely *inside* the existing gated
  block.

- **`FireAndLog` re-entrancy.** I re-walked the call graph for the new
  `folderNew` path:
  - `OnActionInvoked → FireAndLog(HandleVerbAsync)` — outer, no gate.
  - `HandleVerbAsync → CreateNoteAsync → _gate.WithLockAsync(id, …)` — inner, takes gate.
  - On throw inside the gated block: `try/finally` releases the semaphore
    *before* `AsyncSafe.RunAsync` invokes `onError`, which then takes the gate
    *again* sequentially — no re-entrant deadlock.
  - `ContinueWith(...)` runs on `TaskScheduler.Default` after `RunAsync`
    completes (i.e. after `onError` finishes), so `SafePushUpdate` is only ever
    called outside any held gate. Confirmed safe.
  - Auto-refresh at the tail of `CreateNoteAsync` (provider L318) calls
    `FireAndLog(() => RefreshFolderCacheAsync(...))` *outside* the gate (after
    the `await _gate.WithLockAsync(...)` completed) — no nested-gate hazard.
  
  **No re-entrancy bug** introduced by 750f032. N5 (double-push) and N4
  (suppressed onError after delete) remain as the only `FireAndLog` issues, and
  both are unchanged.

---

## 3. Top-3 (this sweep)

1. **N14 — Newly typed folder is persisted to `state.LastFolder` but invisible
   in the dropdown, so the *next* `createNote` silently writes to vault root.**
   HIGH × MED-HIGH. Wrong-folder note placement that the UI affirmatively hides
   from the user. Fix: push the typed folder into `state.RecentFolders` (already
   rendered with the 🕑 prefix) in the success branch, or fall back to
   `state.LastFolder` when `inputs.folder == ""`.

2. **N13 — `inputs.folderNew` missing from the card data payload; binding
   resolves to undefined every render.** HIGH × MED. Any user typing into the
   field while the 2-minute folder-cache timer ticks loses their input. Trivial
   one-line fix in `CardDataBuilder.BuildQuickNoteData`; tested by a JSON
   schema-style assertion on the data block.

3. **N15 — `folderNew` lets the user trivially poison `state.LastFolder` with
   a path the validator rejects.** MED × MED. Promotes the v2 N12 finding from
   "theoretical" to "one typo away," because the dropdown didn't expose typing
   before. Fix: move `state.LastFolder = folder` inside the
   `Status == Created` branch (one-line change, mirrors the existing
   `state.LastCreatedPath` gating).

All three are facets of the same omission: `folderNew` was wired into the
*write* path of `CreateNoteAsync` without reciprocal wiring into the *read*
side (`CardDataBuilder` data shape) or the *commit* side (state-write gating
on `result.Status`). No regression of B1/B2/B3 — the locking and exception
hardening still holds.
