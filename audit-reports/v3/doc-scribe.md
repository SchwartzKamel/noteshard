# doc-scribe v3 — delta audit

**Mode:** read-only. No docs edited.
**Baselines:** [`audit-reports/doc-scribe.md`](../doc-scribe.md) (v1), [`audit-reports/v2/doc-scribe.md`](../v2/doc-scribe.md) (v2).
**Scope this pass (user-directed):**
1. CHANGELOG `[Unreleased]` — does it mention the new `folderNew` field + dropdown restoration?
2. `.github/copilot-instructions.md` — still accurate re: CLI surface + card field ids?
3. README — still accurate re: widget sizes + picking folders?
4. XML `///` residuals on `IStateStore`, `ILog`, `NoteRequest`, `WidgetState`, `CardDataBuilder`.

---

## v2 verification — what moved

| v2 finding | Status | Evidence |
| --- | --- | --- |
| **NH1.** `copilot-instructions.md` line 77 describes Obsidian CLI discovery as "via PATH" while `ResolveExecutable` is a 5-tier preference order with `.cmd`/`.bat` rejection | ⚠️ **Unchanged** | Line 77 is byte-identical to v2. The load-bearing F-02 security invariant still isn't surfaced in the maintainer brief. |
| **NH2.** `AsyncKeyedLock` / `AsyncSafe` / `FireAndLog` / log sanitization not mentioned in `copilot-instructions.md` | ⚠️ **Unchanged** | `Select-String 'AsyncKeyedLock\|AsyncSafe\|FireAndLog\|Sanitize'` over `.github/copilot-instructions.md` → 0 hits. *Key conventions*, *Gotchas*, *Where things live* all silent. |
| **NH3.** README has no mention of `OBSIDIAN_CLI` env-var escape hatch | ⚠️ **Unchanged** | `Select-String 'OBSIDIAN_CLI' README.md` → 0. § Troubleshooting is still 4 bullets. |
| **NM1.** No `tools/README.md` | ⚠️ **Unchanged** | Directory listing: scripts present, no README. |
| **NM3.** Core XML `///` residuals on `IStateStore`/`ILog`/`NoteRequest`/`CliResult` | ⚠️ **Unchanged** — see §M1 below. |
| **NM4.** README omits `make pack-signed` | ⚠️ **Unchanged** | `Select-String 'pack-signed' README.md` → 0 hits. |
| v1 **H3** (no `CONTRIBUTING.md`), **M2** (Troubleshooting), **M3** (ARCHITECTURE stub), **M5** (`tools/WidgetCatalogProbe.cs`) | ⚠️ **All still unchanged.** |
| **NL1–NL3** (CHANGELOG audit hyperlinks, cert-expiry docs, `Concurrency/*` in *Where things live*) | ⚠️ **Unchanged.** |

Nothing from v2's top-3 has been addressed. This pass therefore concentrates on the new user-directed diff and flags one **newly introduced** drift.

---

## NEW HIGH — CHANGELOG `[Unreleased]` is wrong about the folder UI fix

This is the only new HIGH in v3 and it directly answers the user's first check.

### V3-H1. CHANGELOG claims *"Folder ChoiceSet switched from `compact` to `expanded` on medium+large so 'type new or pick' works"* — the current templates are still `compact`, and the actual fix was adding a separate `folderNew` text input

- File: `CHANGELOG.md` line 18:
  > `- Folder ChoiceSet switched from `compact` to `expanded` on medium+large so "type new or pick" works.`
- Reality in source:
  - `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/QuickNote.medium.json:18` → `"style": "compact"` on the `folder` `Input.ChoiceSet`.
  - `src/ObsidianQuickNoteWidget.Core/AdaptiveCards/Templates/QuickNote.large.json:20` → `"style": "compact"` on the `folder` `Input.ChoiceSet`.
  - Both templates **add a new sibling** `Input.Text` with `id: "folderNew"` (medium line 31, large line 33) bound to `${$root.inputs.folderNew}` — placeholder copy *"…or type new folder (optional)"* / *"Optional — overrides picker above"*.
  - Provider consumes it: `src/ObsidianQuickNoteWidget/Providers/ObsidianWidgetProvider.cs:251–254` reads `inputs["folderNew"]` first, falls back to `inputs["folder"]`, then to `state.LastFolder`.
- The implemented pattern is **"compact ChoiceSet + free-text override field"**, not *"ChoiceSet style=expanded"*. Adaptive Cards' `compact` style cannot accept free-text entry at all, so the `expanded` claim wouldn't have solved the "type new" requirement anyway.
- The CHANGELOG also **never names the `folderNew` field** — it's the only new card-data contract added in this line of work, and it's invisible in the release notes.
- Why HIGH: release notes are the first place a downstream reader (or future contributor doing archaeology) looks to understand a UX change. Two separate claims in one bullet are wrong: the style value, and which control enables type-new. This is exactly the "doc-grade contradiction" class v1 flagged for `WidgetsDefinition.xml`.
- Recommended CHANGELOG rewrite (for `release-engineer` to land — doc-scribe should not touch `CHANGELOG.md` per guardrails):
  > - Folder picker on medium/large now pairs the compact `folder` ChoiceSet with a sibling `folderNew` text input (`Input.Text`, `id=folderNew`), letting users either pick an existing folder or type a new one. The provider prefers `folderNew` when non-empty (`ObsidianWidgetProvider.cs:251`).

---

## HIGH — `.github/copilot-instructions.md` is silent on `folderNew` (card-field-id invariant)

### V3-H2. `copilot-instructions.md` § *Adaptive Cards* states the invariant *"if you add a new `${$root.whatever}` binding in a template, add the field to the `JsonObject` there too"* — `folderNew` violates this, and the doc doesn't list the canonical card field ids

- File: `.github/copilot-instructions.md` line 62.
- Template adds `${$root.inputs.folderNew}` (medium + large); `CardDataBuilder.BuildQuickNoteData` does **not** populate `inputs.folderNew` in the emitted `JsonObject` (`src/ObsidianQuickNoteWidget.Core/AdaptiveCards/CardDataBuilder.cs:22–32`). The binding evaluates to empty at render time — presumably intentional ("always show the field blank") — but the repo's own stated invariant says every `${...}` must have a matching key.
- Two possible fixes, both doc-scribe-shaped:
  1. Preferred: add `["folderNew"] = string.Empty` to `CardDataBuilder` (code change — out of doc-scribe's lane, `card-author` territory) so the contract stays regular.
  2. Alternative: amend `copilot-instructions.md` to carve out an exception for "intentionally-always-empty" inputs, with `folderNew` named as the current example.
- Separately, `copilot-instructions.md` has no table or list of the canonical widget-action input ids (`title`, `folder`, `folderNew`, `body`, `tagsCsv`, `template`, `autoDatePrefix`, `openAfterCreate`, `appendToDaily`, `widgetId`, `verb`). New COM verb authors have to grep the templates + provider to learn them. Recommend adding a short *§ Card input ids* table — this is the same shape as the *Obsidian CLI — verified surface* table and would close the "card field ids" accuracy question the user raised.

### V3-H3. `copilot-instructions.md` § *Obsidian CLI — verified surface* line 77 still misdescribes CLI discovery (unchanged from v2 NH1)

Restated because the user explicitly asked whether the CLI surface is still accurate: **no**, and this pass makes no change to that verdict. See v2 NH1 for the full fix. One concrete delta: between v2 and v3 the source-side doc has further hardened — `ObsidianCli.ResolveExecutable` now carries a `<list type="number">` doc comment enumerating all 5 tiers. The XML doc and the markdown brief are now in direct contradiction, not just out of sync.

---

## README — widget sizes ✅, folder picking ⚠️

User's third check. Verdict: **sizes table accurate; folder-picking prose no longer matches the card UX.**

### Widget sizes (README § *Widget sizes*, lines 14–24)

| Claim | Source | Verdict |
| --- | --- | --- |
| "Each size is a distinct Adaptive Card template (`QuickNote.{small,medium,large}.json`)" | `Core/AdaptiveCards/Templates/` directory listing | ✅ three files present |
| Small = "Title field + Create button" | `QuickNote.small.json` | ✅ (not re-dumped this pass; v2 verified) |
| Medium = "Title + folder dropdown + body + Paste / Create buttons" | `QuickNote.medium.json` | ⚠️ **stale — omits the `folderNew` text input** |
| Large = "Full form: title, folder, body, tags, template picker (…), and toggles…" | `QuickNote.large.json` | ⚠️ **stale — omits the `folderNew` text input** |
| Recent Notes separate widget | `QuickNote.small/medium/large.json` vs `RecentNotes.json` | ✅ |
| "folder list is cached and auto-refreshed every 2 minutes (and after every successful create)" | `ObsidianWidgetProvider.cs:31` (`FolderRefreshInterval`) + lines 311–350 (post-create refresh) | ✅ |

### Folder picking

- Medium row in the sizes table says *"folder dropdown"* — true but incomplete; there is now also a *New folder* text field beside it that overrides the dropdown.
- Large row lists *"folder"* in the field catalogue — same gap.
- Recommend README rewrite (one-line edit):
  > | **Medium** | Title + folder picker (pick existing **or type new**) + body + Paste / Create buttons. |
  > | **Large**  | Full form: title, folder picker + type-new field, body, tags, template picker (…), toggles…. |

No other README claim drifted this pass. The CLI-ops section (lines 27–37) still matches `ObsidianCli.cs` verbs. The dev-cert section is unchanged since v2.

---

## M1. XML `///` residuals — unchanged from v2

Fresh counts per user-named file:

| File | `///` lines | Δ vs v2 | Public surface undocumented |
| --- | --- | --- | --- |
| `State/IStateStore.cs` | **0** | — | Interface + all 3 methods. Contract invisible: *Get-never-returns-null*, *Save-is-atomic (`JsonStateStore` writes to `.tmp` + `File.Move`)*, *Delete-on-unknown-id is a no-op*. |
| `Logging/ILog.cs` | **0** | — | `ILog` + `NullLog`. Level semantics, thread-safety, `NullLog.Instance` singleton contract. |
| `Notes/NoteRequest.cs` | **0** | — | `NoteRequest` record (8 members), `NoteCreationStatus` (6 enum values), `NoteCreationResult` (3 members). `AppendToDaily=true` reroutes the pipeline (`NoteCreationService.CreateAsync`) — deserves `<remarks>`. |
| `State/WidgetState.cs` | **1** (type-level only) | — | 16 public properties. Allowed values for `Size` / `Template` and nullability of the four `List<string>` properties (initialised to `new()`, but deserialisation can reset to null) are implicit. |
| `AdaptiveCards/CardDataBuilder.cs` | **4** (type + `CardStatus` only) | — | `BuildQuickNoteData`, `BuildCliMissingData`, `BuildFolderChoices` (private, OK) all method-level-undocumented. The "every `${$root.*}` binding has a field here" invariant called out in `copilot-instructions.md` is nowhere in the XML. |

Total across these five files: **5 `///` lines** covering only type-level summaries on two of them — identical to v2. The highest-leverage single-file fix remains `IStateStore` (3 method `<remarks>` close the mock-seam contract completely).

Example of the shape doc-scribe recommends (not applied this pass — read-only audit):

```csharp
/// <summary>Per-widget JSON-backed persistence abstraction. Implementations must be thread-safe.</summary>
public interface IStateStore
{
    /// <summary>Returns the current state for <paramref name="widgetId"/>, or a freshly-initialised default
    /// (never <see langword="null"/>) if none exists.</summary>
    WidgetState Get(string widgetId);

    /// <summary>Atomically persists <paramref name="state"/>. Implementations must swallow IO exceptions —
    /// widget code relies on this being total (see ObsidianWidgetProvider "widget must never crash over
    /// state persistence").</summary>
    void Save(WidgetState state);

    /// <summary>Removes persisted state for <paramref name="widgetId"/>. No-op if none exists.</summary>
    void Delete(string widgetId);
}
```

---

## Cross-link rot check (delta from v2)

All v1/v2-verified links still resolve. New surface this pass: `folderNew` binding — not yet cross-linked anywhere in docs. No broken links.

---

## Top 3 — v3

1. **V3-H1. CHANGELOG `[Unreleased]` line 18 is wrong in two ways.** It claims `compact → expanded` (not true — both templates are still `compact`) and it never names the `folderNew` input that actually enables the "type new or pick" UX. Out of doc-scribe's lane to edit (`release-engineer`), but flagged because it's the freshest drift in the repo and affects downstream release-note readers.
2. **V3-H2. `copilot-instructions.md` has no table of card input ids, and `folderNew` silently violates the repo's own "every `${...}` binding gets a `CardDataBuilder` field" invariant.** Adding a *§ Card input ids* table (analogous to *Obsidian CLI — verified surface*) and either populating `inputs.folderNew` in `CardDataBuilder` or carving out a named exception for it would resolve both. This directly answers the user's "card field ids" check.
3. **README + copilot-instructions.md folder-picking drift.** README § *Widget sizes* rows for Medium and Large say "folder dropdown" / "folder" with no mention of the parallel *New folder* text input (one-line edit each). Combined with V3-H1/H2 this is the "user-visible half" of the folderNew documentation gap. Fixing these three cross-document sites in one PR closes the entire folderNew documentation story.

*(Residuals from v2 — NH1 CLI 5-tier resolution order, NH2 concurrency invariants, NH3 `OBSIDIAN_CLI` escape hatch, M1 Core XML `///` — are still the highest-leverage doc edits in absolute terms, but the user's v3 prompt asked about a narrower diff so they're kept in the *v2 verification* table above rather than re-ranked here.)*

---

## Deliverables from this audit

- This report: `audit-reports/v3/doc-scribe.md`.
- No source or doc files modified.
- Todo `v3-doc-scribe` will be marked `done`.
