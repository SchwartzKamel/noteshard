# doc-scribe v2 — verification + new gaps

**Mode:** read-only. No docs were edited.
**Baseline:** [`audit-reports/doc-scribe.md`](../doc-scribe.md) (v1).
**Scope this pass:** verify v1 fixes; audit new tooling (`tools/New-DevCert.ps1`, `tools/Sign-DevMsix.ps1`, random-password flow, `OBSIDIAN_CLI` env var); audit `.github/copilot-instructions.md` against current invariants (`AsyncKeyedLock`, `AsyncSafe`, `FireAndLog`, sanitized logs, CLI PATH resolution order); re-check Core XML `///` coverage.

---

## v1 verification — what was fixed

| v1 finding | Status | Evidence |
| --- | --- | --- |
| **H1.** Stale "66 xUnit tests" comment in `copilot-instructions.md` | ✅ **Fixed** | Line 13 now reads `# full test suite (xUnit, Core only)` — hard count removed. (Actual count today: 108 `[Fact]/[Theory]` attributes, validating the earlier recommendation to drop the literal.) |
| **H2.** `WidgetsDefinition.xml` contradicted `Package.appxmanifest` | ✅ **Fixed** | File no longer present at `src/ObsidianQuickNoteWidget/`. CHANGELOG `[Unreleased]` records *"Orphan WidgetsDefinition.xml deleted (doc-scribe, code-archaeologist)"*. |
| Dev-cert password literal in docs (security-auditor F-01 cross-cut) | ✅ **Fixed** | README § *Dev cert + signing* and `copilot-instructions.md` § *Reinstalling the MSIX during development* both describe the random-24-char `password.txt` with user-only ACL flow. No literal password anywhere in the repo's docs. |
| **M4.** No `CHANGELOG.md` | ✅ **Fixed** | `CHANGELOG.md` present, well-formed: Keep-a-Changelog v1.1.0 + SemVer headers, `[Unreleased]` section with `Fixed` / `Security` subsections, prior `[0.1.0]` release block. Per-entry attribution to source audits is a nice touch. |

### Still outstanding from v1 (unchanged)

| v1 finding | Status |
| --- | --- |
| **H3.** README points contributors at `copilot-instructions.md` but no `CONTRIBUTING.md` exists | ⚠️ Unchanged |
| **M2.** README § Troubleshooting still 5 bullets; no `docs/TROUBLESHOOTING.md` | ⚠️ Unchanged |
| **M3.** No `ARCHITECTURE.md` stub pointing humans at `.github/copilot-instructions.md` | ⚠️ Unchanged |
| **M5.** `tools/WidgetCatalogProbe.cs` still loose at repo root, undocumented | ⚠️ Unchanged (file present; not referenced in README or `copilot-instructions.md`) |
| **L1–L7** nice-to-haves | ⚠️ Unchanged |

---

## NEW HIGH — invariants drift since v1

### NH1. `copilot-instructions.md` § *Obsidian CLI — verified surface* still claims `obsidian` is "discovered via PATH" — actual resolution order is now richer

- File: `.github/copilot-instructions.md` line 77
  > "**Executable:** `C:\Program Files\Obsidian\Obsidian.com` (also `Obsidian.exe` — same binary). Discovered via `PATH` after the user runs *Settings → General → Command Line Interface → Register CLI*."
- Reality: `src/ObsidianQuickNoteWidget.Core/Cli/ObsidianCli.cs:203–258` (`ResolveExecutable`) implements a **5-tier preference order** (and security-auditor F-02 specifically tightened this):
  1. `OBSIDIAN_CLI` env var override (if file exists).
  2. `%ProgramFiles%\Obsidian\Obsidian.(com|exe)`.
  3. `%LocalAppData%\Programs\Obsidian\Obsidian.(com|exe)`.
  4. `HKCU\Software\Classes\obsidian\shell\open\command` registry default value.
  5. `PATH` scan, **`.com` / `.exe` only — `.cmd` / `.bat` are explicitly rejected** (F-02). Emits a one-shot `Warn`: *"Resolved 'obsidian' via PATH (...) — consider setting OBSIDIAN_CLI to a fully-qualified path"*.
- Why HIGH: PATH is now the **last** fallback, and the rejection of `.cmd`/`.bat` is a load-bearing security invariant (planted-binary attack vector). Documenting it as the primary discovery path actively misleads anyone debugging "wrong obsidian binary picked up".
- Fix: rewrite the bullet as a numbered list mirroring `ResolveExecutable`'s `<list type="number">` doc-comment, and call out the `.com`/`.exe`-only PATH constraint.

### NH2. `copilot-instructions.md` § *Key conventions* / *Gotchas* never mentions `AsyncKeyedLock`, `AsyncSafe`, `FireAndLog`, or log sanitization — all four are now load-bearing repo invariants

These four primitives were introduced after v1 (bug-hunter B1/B2/B3 + security-auditor F-03) and are referenced from 7+ source sites and 3 dedicated test files:

| Invariant | Where enforced | Why a maintainer must know |
| --- | --- | --- |
| **`AsyncKeyedLock<string>` per-widget gate** | `Providers/ObsidianWidgetProvider.cs:33` (field), wrapped around every `Get → mutate → Save` in `OnActionInvoked`, `WidgetContextChanged`, `Activate`, `Deactivate` (lines 67, 86, 116, 133, 367) | Bug-hunter B1: without this, concurrent inbound COM calls on the same `widgetId` race on `state.json`. New COM verb handlers MUST acquire `_gate.WithLockAsync(widgetId, …)`. |
| **`AsyncSafe.RunAsync(...)` wrapper** | `Concurrency/AsyncSafe.cs`; called inside `FireAndLog` (line 162) and at line 226 in the provider | Bug-hunter B3: every fire-and-forget continuation MUST go through this — it catches, logs, surfaces `LastError` to state, and prevents unobserved `Task` exceptions from killing the COM server. |
| **`FireAndLog(work, widgetId, context, pushUpdateOnCompletion)`** | `ObsidianWidgetProvider.cs:160` (definition), 7 call sites | The single sanctioned way to launch background work from a synchronous COM verb. Any new `_ = SomeAsync()` in widget code is a regression. |
| **`FileLog.SanitizeForLogLine`** | `Logging/FileLog.cs:36` | Security-auditor F-03: every log line passes through this — replaces CR/LF/control chars to prevent log-injection. New log call sites get it for free via `Write(...)`; **direct stream writes bypass it**. |

- Why HIGH: `copilot-instructions.md` is the maintainer brief. A new contributor adding a COM verb, a background timer, or a custom log sink will silently violate any of these. None are surfaced.
- Recommended fix: add a short *§ Concurrency & logging invariants* between *Key conventions* and *Obsidian CLI — verified surface* with one line per primitive and the file path. This is the highest-leverage doc edit available right now.

### NH3. README has zero mention of `OBSIDIAN_CLI`

- File: `README.md` § *Requirements* / § *Troubleshooting*.
- The env-var override is the **only** documented escape hatch when (a) the user has multiple Obsidian installs, (b) PATH points at the wrong one, or (c) the on-by-default warning *"consider setting OBSIDIAN_CLI to a fully-qualified path"* fires. Today the user has to read source to discover it.
- Fix: one bullet under § Troubleshooting:
  > If the widget picks the wrong `obsidian.exe`, set `OBSIDIAN_CLI` to a fully-qualified path (e.g. `C:\Program Files\Obsidian\Obsidian.com`). The widget checks this env var before any other discovery mechanism.

---

## NEW MEDIUM

### NM1. `tools/New-DevCert.ps1` and `tools/Sign-DevMsix.ps1` are referenced by name in both README and `copilot-instructions.md` but have no `tools/README.md`

- Both scripts exist (`Get-ChildItem tools\` confirms), and the prose is accurate at the *invocation* level: `.\tools\New-DevCert.ps1` produces `dev.pfx` + `password.txt` (24-char random, user-only ACL) under `%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\`; `.\tools\Sign-DevMsix.ps1 <msix>` consumes it.
- Gaps a contributor would still hit:
  - **Cert lifetime / rotation:** README says "90-day validity" — what happens on day 91? Is `New-DevCert.ps1` idempotent? (Doc claim unverified — script not inspected this pass.)
  - **`.cer` companion:** README § *Dev cert + signing* tells the user to install the `.cer` into Trusted People, but `New-DevCert.ps1`'s output filename for the `.cer` is not stated. Implicit but undocumented.
  - **`make pack-signed` refusal logic:** both docs assert it "refuses any `SIGNING_CERT` path under `dev-cert\`" — Makefile target should be cross-linked from `Sign-DevMsix.ps1` documentation so a release engineer sees the boundary.
  - **Cleanup:** no documented way to revoke / regenerate. (`Remove-Item` the folder? rerun `New-DevCert.ps1`?)
- Recommend a small `tools/README.md` table: `script | purpose | inputs | outputs | rerun behavior`.

### NM2. Random-password flow is described in two places with subtly different wording — risk of drift

- README line 66: *"writes a fresh random 24-character password to `…\dev-cert\password.txt` with a user-only ACL. The password is **generated on first run and never printed, committed, or shared**"*.
- `copilot-instructions.md` line 37: *"alongside a `password.txt` containing a freshly generated random 24-character password with a user-only ACL. The password is never printed or committed"*.
- Both are correct but neither says **what mechanism enforces user-only ACL** (icacls? `Set-Acl`?) or **what happens if `password.txt` is deleted while `dev.pfx` survives** (does `Sign-DevMsix.ps1` regenerate, fail, or prompt?). Source-of-truth should be the script's own header comment; docs should defer to it.
- Severity MEDIUM not HIGH because the security-relevant claim ("never printed/committed") *is* surfaced loudly in both files — only operational details are missing.

### NM3. Core public-surface XML `///` coverage — almost entirely unchanged from v1

Re-counted `///` lines per file (v1 numbers in parens):

| File | `///` lines | Δ vs v1 | Status |
| --- | --- | --- | --- |
| `State/IStateStore.cs` | 0 (0) | — | **Still undocumented** |
| `Logging/ILog.cs` | 0 (0) | — | **Still undocumented** |
| `State/WidgetState.cs` | 1 (1) | — | Type-level only; 16 properties undocumented |
| `Notes/NoteRequest.cs` | 0 (0) | — | DTO + 2 enums undocumented |
| `Cli/CliResult.cs` | 0 (0) | — | `ExitCode=-1` sentinel undocumented |
| `AdaptiveCards/CardDataBuilder.cs` | 4 (4) | — | Type-level only |
| `AdaptiveCards/CardTemplates.cs` | 1 (1) | — | `LoadForSize` fallback undocumented |
| `Notes/NoteCreationService.cs` | 5 (5) | — | `CreateAsync` no `<param>`/`<returns>` |
| `Notes/FilenameSanitizer.cs` | 2 (2) | — | Rules undocumented |
| `Notes/NoteTemplates.cs` | 0 (0) | — | Enum undocumented |
| `Notes/FrontmatterBuilder.cs` | 4 (4) | — | `ParseTagsCsv` rules undocumented |
| `Notes/DuplicateFilenameResolver.cs` | 4 (4) | — | Suffix scheme undocumented |
| `Logging/FileLog.cs` | 10 (4) | **+6** ✅ | Improved — now documents `SanitizeForLogLine`, rollover, paths |
| `Cli/ObsidianCli.cs` `ResolveExecutable` | (new) | **+** ✅ | New `<list type="number">` doc covering all 5 resolution tiers (cross-link target for NH1's fix) |

Net: 2 files improved, 11 still at v1 levels. Recommend a focused pass on the four highest-leverage seams: `IStateStore`, `ILog`, `NoteRequest`, `CliResult` — these are the mock seams for the entire test suite and the "stable Core API" the maintainer brief promises.

### NM4. README § *Build, test, package* doesn't mention `make pack-signed`

- README only documents `make build` / `make test` / `make pack`.
- `copilot-instructions.md` line 37 references `make pack-signed` and its refusal-of-dev-cert guard. Discoverability gap for release contributors.

---

## NEW LOW

- **NL1.** `CHANGELOG.md` `[Unreleased]` cites audit findings by ID (B1, B2, B3, F-01, F-02, F-03) but the audit reports themselves are under `audit-reports/` — no link from CHANGELOG to the source audit. One-time cross-link would help future archaeology.
- **NL2.** README § *Dev cert + signing* describes the dev-cert lifecycle but doesn't say what to do when the 90-day cert expires (re-run `New-DevCert.ps1`? Will `Sign-DevMsix.ps1` warn before expiry?). Friendly user-facing addition.
- **NL3.** `copilot-instructions.md` § *Where things live* should add `src/ObsidianQuickNoteWidget.Core/Concurrency/AsyncKeyedLock.cs` and `AsyncSafe.cs` — the most-bug-prone file claim in v1 (`Com/ClassFactory.cs`) now has competition.

---

## Cross-link rot check (delta from v1)

All v1-verified links still resolve. New surface introduced this round:

| From | To | Status |
| --- | --- | --- |
| `README.md` § Dev cert + signing | `tools/New-DevCert.ps1` | ✅ file exists |
| `README.md` § Dev cert + signing | `tools/Sign-DevMsix.ps1` | ✅ file exists |
| `copilot-instructions.md` § Reinstalling… | `tools/New-DevCert.ps1` | ✅ |
| `copilot-instructions.md` § Reinstalling… | `tools/Sign-DevMsix.ps1` | ✅ |
| `CHANGELOG.md` audit refs (B1/B2/B3/F-01/F-02/F-03) | `audit-reports/bug-hunter.md`, `audit-reports/security-auditor.md` | ✅ files exist (no actual hyperlink — see NL1) |

No broken links. No newly-orphaned docs.

---

## Top-3 (this pass)

1. **NH1 — `copilot-instructions.md` describes Obsidian CLI discovery as "via PATH"; reality is a 5-tier preference order with `.cmd`/`.bat` rejection.** This is now a security-relevant invariant (F-02). Replace the line-77 bullet with a numbered list mirroring `ObsidianCli.ResolveExecutable`. Highest leverage because it both removes a misleading claim and documents a hardened invariant in one edit.
2. **NH2 — `AsyncKeyedLock` / `AsyncSafe` / `FireAndLog` / log sanitization are nowhere in `copilot-instructions.md`.** A new contributor adding a COM verb or background task will silently violate B1/B3/F-03. Add a short *§ Concurrency & logging invariants* section listing each primitive, file path, and the rule it enforces.
3. **NH3 + NM3 combined — escape hatches and stable seams.** Surface `OBSIDIAN_CLI` in README § Troubleshooting (one bullet) **and** add XML `///` to `IStateStore`, `ILog`, `NoteRequest`, `CliResult`. Together these close the two doors a maintainer hits first when something is wrong: "how do I override?" and "what does this seam guarantee?".

---

## Deliverables from this audit

- This report: `audit-reports/v2/doc-scribe.md`.
- No source or doc files modified.
- Todo `v2-doc-scribe` marked `done`.
