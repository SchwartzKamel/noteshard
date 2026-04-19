# Test-Author Coverage Audit — v2 (delta sweep)

**Scope:** `src/ObsidianQuickNoteWidget.Core`, `src/ObsidianQuickNoteWidget`, `src/ObsidianQuickNoteTray`
**Test project:** `tests/ObsidianQuickNoteWidget.Core.Tests` (xUnit, net10.0, coverlet)
**Suite size:** 199 tests passing (verified `dotnet test`).
**Mandate:** Read-only. Inventory new tests added since v1, mark each prior gap COVERED / PARTIAL / UNCOVERED, identify new uncovered surfaces introduced by new code, and propose a fresh Top-3.

---

## 1. New tests inventoried this pass

| File | Cases | Notes |
|---|---|---|
| `PerWidgetGateTests.cs` (NEW) | 7 | `AsyncKeyedLock<TKey>`: same-key serialization, distinct-key concurrency, refcount cleanup, lingering-while-held, generic overload return, exception releases lock, cancellation does not leak refcount. |
| `AsyncSafeTests.cs` (NEW) | 4 | Success path, exception → onError + log, onError-throws is swallowed, no-onError still logs. |
| `FileLogTests.cs` (NEW) | 5 | CRLF-in-message escaped to literal (CWE-117), tabs preserved, UTF-8 round-trip, other C0 controls → `\uXXXX`, exception text sanitized when logging `Error(msg, ex)`. |
| `ObsidianCliResolutionTests.cs` (NEW) | 8 facts + 7-row theory | `ResolveExecutable` precedence: env-var override > known install paths > registry > PATH; `.cmd`/`.bat` rejected; one-shot PATH warning; `ExtractExeFromRegistryCommand` parser theory. |
| `CardTemplatesTests.cs` (extended) | +schema-version + every-action-binds-widgetId facts | Reinforces structural assertions. |
| `CardDataBuilderTests.cs` (extended) | +`CliMissingData` theory rows, `EmitsWidgetId_FromState` | |
| `NoteCreationServiceTests.cs` (extended) | +`AppendToDaily_*` (4), +`CliAutoRenamesOnCollision`, +`CliReportsStdoutError`, +`NoCollision_WhenVaultRootNull`, +`CliReturnsNullPath` | |
| `ObsidianCliParsersTests.cs` (extended) | +`TryParseAppendedDaily`, +`HasCliError` theories, +CRLF/empty-input rows | |

Total assertions added since v1 ≈ 60+. Suite went from ~135 → **199**.

---

## 2. Coverage delta vs prior Top-10 gaps

| # | Prior gap | v2 status | Evidence / residual |
|---|---|---|---|
| 1 | `ObsidianWidgetProvider.ParseInputs` | **UNCOVERED** | Helper still lives in Widget assembly; no `tests/ObsidianQuickNoteWidget.Tests` project exists. Zero callers in the new test inventory reference `Providers`. |
| 2 | `ObsidianWidgetProvider.RememberRecent` | **UNCOVERED** | Same — Widget assembly has no test project; helper is `private static`, no `InternalsVisibleTo`. |
| 3 | `NoteCreationService.BuildBody` | **PARTIAL** | Only `Create_TemplateSeedsBody` (1 fact, `Meeting` only) exercises the seeded-body path. The `seeded.TrimEnd() + "\n\n" + user` branch is only hit for `Meeting` with a null/empty body — the `else` arm (both seeded and user non-empty) is structurally exercised but not asserted on the join shape. `Blank+body`, `non-Blank+empty body`, and the exact `\n\n` separator are still un-asserted. The `==`/`!=` mutation on `seeded.Length == 0` would survive. |
| 4 | `NoteTemplates.SeedBody` arms + default | **PARTIAL** | `Meeting` arm contains "Attendees" — asserted via substring. `Daily`, `Book`, `Idea`, `Blank`, and `default(NoteTemplate)999` arms remain unverified. Swap-arm mutation `Book↔Idea` survives. |
| 5 | Template-tag / user-tag case-insensitive dedup | **UNCOVERED** | `Create_TemplateSeedsBody` asserts `Contains("meeting")` only; it does not vary `TagsCsv` casing, so the `OrdinalIgnoreCase → Ordinal` mutation on `existing.Equals(t, ...)` (NoteCreationService.cs:62) still survives. |
| 6 | `FolderPathValidator` boundaries | **PARTIAL** (unchanged) | No new rows added to `FolderPathValidatorTests`. `"/"` alone, `"a//b"`, `"X:"` 2-char drive, leading-space segment still unverified. |
| 7 | `ObsidianWidgetProvider.ParseBool` | **UNCOVERED** | Same blocker as #1/#2 — Widget assembly has no test project. |
| 8 | `FrontmatterBuilder.YamlQuote` per-trigger | **PARTIAL** (unchanged) | `Build_QuotesSpecialChars` is still a single `[Fact]`; no theory across `: # , [ ] { } & * ! \| > ' " % @ \``; empty-tag forced-quote and backslash+quote escape order still unverified. |
| 9 | `FilenameSanitizer` `COM0`/`LPT0` + 120-boundary | **UNCOVERED** (unchanged) | No additions to `FilenameSanitizerTests.cs` this pass. |
| 10 | `FileLog.Roll` 1MB boundary | **UNCOVERED** | New `FileLogTests.cs` covers `SanitizeForLogLine` (CWE-117 hardening) but does **not** trigger `Roll()`: no test pre-seeds the log file past `MaxBytes`, no test asserts existence of `<path>.1`, no test asserts the strict `>` (vs `>=`) boundary at exactly 1,000,000 bytes. |

**Summary:** Of 10 prior gaps, **0 fully covered**, **5 partial**, **5 still uncovered**. The new sweep tackled adjacent surfaces (concurrency, CLI resolution, log sanitization, CLI append/error parsing) rather than the prior Top-3 — which all remain in the Top-3 of v2 with minor reordering.

---

## 3. New uncovered surfaces introduced by the new code

### 3.1 `AsyncKeyedLock<TKey>` edge cases beyond `PerWidgetGateTests`

| Branch / behavior | Source location | Why it matters | Killable mutation |
|---|---|---|---|
| `Acquire` lost-race retry — `existing.Disposed == true` then `continue` | `AsyncKeyedLock.cs:81–88` | The dispose-then-retry loop is the heart of correctness under contention; never exercised by current tests (all keys converge before any release). | Replace `continue` with `return existing` → would hand back a disposed semaphore. |
| `Acquire` `TryAdd` racer losing branch — `fresh.Semaphore.Dispose()` | `AsyncKeyedLock.cs:93–94` | If two threads create entries concurrently for a fresh key, the loser must dispose its un-published semaphore; no test forces the race. | Drop the `Dispose()` call → semaphore handle leak (silent). |
| Custom `IEqualityComparer<TKey>` ctor | `AsyncKeyedLock.cs:17–20` | Provider uses `StringComparer.OrdinalIgnoreCase` (`ObsidianWidgetProvider.cs:33–34`). Tests construct only the default-comparer overload, so swapping the field to `EqualityComparer<TKey>.Default` would survive. | Drop the comparer parameter → "Widget-A" vs "widget-a" no longer share a gate; only mixed-case test would catch. |
| `ArgumentNullException.ThrowIfNull(work)` guards on both overloads | `AsyncKeyedLock.cs:27, 51` | Trivial guards but currently unverified. Removing them yields an opaque NRE inside the lock instead of an immediate ArgumentNullException. | Removal would survive unless asserted. |
| Generic overload `WithLockAsync<TResult>` cancellation + exception paths | `AsyncKeyedLock.cs:49–71` | Only `GenericOverload_ReturnsValue` (happy path) covers it. The `catch { Release(...); throw; }` block on the generic overload is unverified — a regression copy-pasting only the non-generic fix would survive. | Swap `acquiredSemaphore: false` → `true` in the generic overload's catch → tests would still pass, future deadlock. |

### 3.2 `IObsidianCliEnvironment` registry parser path

| Surface | Status |
|---|---|
| `ObsidianCli.ExtractExeFromRegistryCommand` static parser | **COVERED** by 7-row theory (null/empty/whitespace/quoted/unquoted/bare/unterminated). |
| `DefaultObsidianCliEnvironment.GetObsidianProtocolOpenCommand` — non-Windows early null-return (line 30) | **UNCOVERED**. Unreachable from xUnit on Windows; either skip-on-Linux test or extract pure helper. |
| `DefaultObsidianCliEnvironment.GetObsidianProtocolOpenCommand` — registry try/catch swallowing (line 37–40) | **UNCOVERED**. Currently no seam to inject a `Microsoft.Win32.Registry` failure; missing-key path returns null naturally but a *throwing* `OpenSubKey` (e.g., security exception) is unverified. |
| `DefaultObsidianCliEnvironment.FileExists` whitespace-guard (line 26) | **PARTIAL** — covered indirectly by `FakeEnv.FileExists` mirroring the same guard, but the production class itself is never instantiated in a test. |
| `ResolveExecutable` PATH `.com` extension acceptance | **PARTIAL** — only `.exe` rows in `PathScanAcceptsComAndExeOnly_AndWarnsOnce`; the `.com` arm of `WindowsPathExtensions` for the *PATH* fallback (vs the known-install path) is not asserted. A mutation dropping `.com` from `WindowsPathExtensions` would survive on the PATH branch. |
| `ResolveExecutable` non-Windows branch (`UnixPathExtensions = [""]`) | **UNCOVERED**. `FakeEnv.IsWindows` defaults to `true` and no test sets it `false`. The Linux `obsidian` (no extension) lookup path is dead-code from the test's perspective. |

### 3.3 `FileLog` exception path for unwritable file

| Surface | Status |
|---|---|
| `Write` `try { Roll(); File.AppendAllText(...) } catch { }` swallow (lines 73–80) | **UNCOVERED**. No test holds an exclusive lock on the log file then calls `Info`/`Warn`/`Error`. The contract "logging never throws" is asserted only implicitly via tests that don't induce a failure. A mutation removing the outer `try/catch` would only surface if a test simulates a locked or read-only file. |
| `Roll` inner `try { ... } catch { /* ignore */ }` (lines 86–95) | **UNCOVERED**. Roll itself is never invoked in tests (file never exceeds 1 MB). The boundary `Length > MaxBytes` (strict `>`), the `if (File.Exists(old)) File.Delete(old)` overwrite path, and the `File.Move` step are all unverified. Mutations `> → >=`, dropping the pre-existing-`.1` delete, or swapping `Move` for `Copy` all survive. |
| `FileLog` constructor — `Directory.CreateDirectory` on a path whose parent is read-only | **UNCOVERED**. Constructor would throw; production callers never wrap. Low priority — not a behavior promise of the type. |

### 3.4 `AsyncSafe.RunAsync` minor gaps

- No `[Theory]` row for `OperationCanceledException` rethrow vs swallow contract — currently any exception including OCE is logged + handler-invoked. If the contract is "OCE should propagate", the implementation would silently swallow it; tests don't pin the policy either way.
- `AsyncSafe` only has a non-generic `RunAsync` (verified by grep). No new uncovered surface from a generic overload.

### 3.5 Provider-level surfaces that the new gating code now makes testable

The new internal `FireAndLog` method (`ObsidianWidgetProvider.cs:160`) is now `internal` and has a constructor accepting `(ILog, IStateStore, IObsidianCli?)`. **A widget-side test project would now be able to drive `FireAndLog` directly** with fake `ILog`/`IStateStore`/`IObsidianCli` and assert the onError → state-mutation → SafePushUpdate sequence — but no such project exists yet. This is a leverage opportunity, not a regression.

---

## 4. Fresh Top-3 (highest leverage now)

The prior P0-1/P0-2/P0-3 trio is unchanged in priority but joined/displaced by two surfaces that the new code introduced or exposed.

### **Top-1 · `NoteCreationService.BuildBody` composition contract** (was P0-3, now most actionable)
- **Why now:** All scaffolding exists (FakeCli captures `CreatedBody`, `FixedTimeProvider` pins date). Adding a `[Theory]` over `NoteTemplate × {empty, "user text"}` requires zero new test infrastructure. The seeded-body composition is the silent-corruption hot-spot for every templated note.
- **Mutations the new tests would kill:** swap-order, `TrimEnd()` removal, `\n\n → \n` separator change, `seeded.Length == 0` ↔ `user.Length == 0` guard swap.
- **Side benefit:** The same theory implicitly covers `NoteTemplates.SeedBody`'s 4 untested arms via the seeded-prefix capture, collapsing prior gaps #3 and #4 into one test.

### **Top-2 · `ObsidianWidgetProvider.{ParseInputs, ParseBool, RememberRecent}` trio** (was P0-1/P0-2/P1-7)
- **Why now:** `internal` constructor exists for the provider, the per-widget gate is already extracted to Core, and the static helpers are pure. The remaining blocker is a one-line `[assembly: InternalsVisibleTo("ObsidianQuickNoteWidget.Tests")]` plus a new test project (or fold into Core.Tests if a project ref to the Widget assembly is acceptable). With three tightly-scoped pure helpers, a single new test file covers all three at ~15 cases. Highest mutation-yield-per-line in the entire codebase.
- **Mutations killed:** see v1 §P0-1/P0-2/P1-7 — unchanged.

### **Top-3 · `FileLog.Roll` boundary + unwritable-file swallow contract** (promoted from P3-10)
- **Why now:** The new `FileLogTests.cs` already establishes the test fixture (temp-dir + cleanup), so the marginal cost of three `Roll`-boundary facts is near zero. Combined with one fact that opens the file `FileShare.None` from a second process/handle and asserts no exception escapes, this nails down both the rotation contract (size-driven, strict `>`, pre-existing `.1` overwritten) and the "logging never throws" promise that the type's xmldoc asserts but no test enforces.
- **Mutations killed:** `> → >=` boundary, dropping the `if (File.Exists(old)) File.Delete(old)` guard, swapping `File.Move` for `File.Copy`, removing the outer `try/catch` in `Write`.

---

## 5. Deliberately deferred (still skipped)

| Area | Reason |
|---|---|
| Widget COM lifecycle (`CreateWidget`, `Activate`, `PushUpdate`) | Entangled with `WidgetManager.GetDefault()`; needs a host-side seam — `refactorer` work, not test-author. |
| Tray `QuickNoteForm` | WinForms; needs `QuickNoteController` extraction first. |
| `ObsidianCli.RunAsync` process spawning | Requires `IProcessRunner` extraction; out of scope for read-only sweep. |
| Non-Windows arms of `ResolveExecutable` (`UnixPathExtensions`) | Project targets `net10.0-windows*` for the widget; covering this would need `[Trait("Platform","Linux")]` + CI matrix. Flag for `release-engineer`. |
| `AsyncSafe` OCE propagation policy | Behavioral contract is genuinely undecided in the SUT; raise with `bug-hunter` before pinning a test. |

---

## 6. Numbers at a glance

- Prior gaps: **10**
- Now COVERED: **0**
- Now PARTIAL: **5** (#3 BuildBody, #4 SeedBody, #6 FolderPathValidator, #8 YamlQuote, plus residuals on FileLog)
- Still UNCOVERED: **5** (#1 ParseInputs, #2 RememberRecent, #5 template-tag dedup, #7 ParseBool, #9 FilenameSanitizer reserved-name boundaries, #10 FileLog.Roll)
- New uncovered surfaces from new code: **8** (AsyncKeyedLock dispose-race retry, TryAdd loser dispose, custom-comparer, generic-overload acquire-failure path, OCE policy in AsyncSafe, FileLog write-swallow on locked file, FileLog.Roll boundary, registry env non-Windows + throw paths)
- Suite size: **199** (verified passing)

The next sweep should treat **Top-1 (BuildBody)** and **Top-2 (Widget pure helpers via new test project)** as a single PR — together they would close 4 of the 5 still-UNCOVERED prior gaps in one stroke.
