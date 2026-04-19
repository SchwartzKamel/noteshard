# Obsidian CLI — Surface Report v3 (nested-folder spot-check)

- **Resolved executable:** `C:\Program Files\Obsidian\Obsidian.com`
- **Obsidian version:** `1.12.7 (installer 1.12.7)` — **unchanged** since v1/v2
- **Vault under test:** `C:\Users\lafia\OneDrive\Documents\Obsidian\Vaults\lafiamafia`
- **Scratch folder:** `audit-v3/deep/nested/` — created by the probe, fully removed at cleanup
- **Probe date:** 2026-04-19
- **Scope:** targeted re-probe to (a) re-verify the three v1 drifts remain closed (per v2's code audit) and (b) answer one new question — *does `obsidian create` auto-create missing intermediate folders?* — which directly shapes the `folderNew` UX.

---

## 1. Verification of v2's "all three drifts closed" claim

v2 audited the code and re-probed `create`, `open`, `daily:append`, `daily:path`, `version`; this v3 sweep re-ran the minimal trio that matters for the folder-UX question. Observed stdout is **byte-identical** to v1/v2 for every re-probed shape:

| Command | Observed stdout | exit | v1/v2 shape? |
| --- | --- | --- | --- |
| `obsidian version` | `1.12.7 (installer 1.12.7)` | 0 | ✅ identical |
| `obsidian vault info=path` | `C:\Users\lafia\OneDrive\Documents\Obsidian\Vaults\lafiamafia` | 0 | ✅ identical |
| `obsidian folders` (pre-probe) | `/` + `Test` (one per line) | 0 | ✅ identical |
| `obsidian create path=audit-v3/deep/nested/new-note.md content=v3-probe` | `Created: audit-v3/deep/nested/new-note.md` | 0 | ✅ success prefix unchanged |
| `obsidian delete path=audit-v3/deep/nested/new-note.md permanent` | `Deleted permanently: audit-v3/deep/nested/new-note.md` | 0 | ✅ identical |

**Verdict:** Obsidian version, stdout shapes, exit-code-always-0 contract, and the success-prefix parsing surface are **stable**. v2's conclusion that the three v1 drifts (input-path echo on collision, exit-code-as-success, `OpenNoteAsync("")` → bogus `vault` fallback) are resolved in `ObsidianCli.cs` / `ObsidianCliParsers.cs` still holds — no new drift detected that would reopen any of them.

---

## 2. New finding — nested folder auto-creation

**Question:** When `create path=<a>/<b>/<c>/note.md` is invoked and *none* of `<a>`, `<a>/<b>`, `<a>/<b>/<c>` exist, does the CLI (a) error, (b) create only the file, or (c) create the full chain?

**Probe (real stdout, verbatim):**

```text
> obsidian folders                          # pre-state
/
Test

> obsidian create path=audit-v3/deep/nested/new-note.md content=v3-probe
Created: audit-v3/deep/nested/new-note.md       exit=0

> obsidian folders                          # post-state
/
audit-v3
audit-v3/deep
audit-v3/deep/nested
Test
```

**Filesystem confirmation** after the single `create` call:

```
C:\...\Vaults\lafiamafia\audit-v3\
C:\...\Vaults\lafiamafia\audit-v3\deep\
C:\...\Vaults\lafiamafia\audit-v3\deep\nested\
C:\...\Vaults\lafiamafia\audit-v3\deep\nested\new-note.md   (8 bytes)
```

**Answer: (c) — the CLI auto-creates every intermediate folder in a single `create` call.** Three brand-new directories materialised from one invocation; `folders` immediately reflected them. No pre-`mkdir`-style step is required, no error is raised, no confirmation flag is needed, and the `Created:` prefix returns the full nested vault-relative path exactly as supplied.

### Corollary quirks worth surfacing

- **N-01 (folder-list cache lag on deletion).** After the nested note was deleted (`obsidian delete … permanent`) and the now-empty `audit-v3/…` directories were removed from the filesystem, `obsidian folders` *still* listed the three ghost folders for ~1–2 seconds. A second call after a brief settle returned the correct pre-probe list (`/`, `Test`). Filesystem was the source of truth throughout. **Implication:** callers that read `folders` immediately after a filesystem-level mutation may see stale entries for a short window. `create` itself does not seem to exhibit the lag — post-create `folders` was correct immediately.
- **N-02 (`vault info=folders` is a count, not a list).** `obsidian vault info=folders` returns a single integer (`1` after cleanup), not folder names. This is already implied by the v1 TSV probe but worth re-stating: use `obsidian folders` for enumeration, `vault info=folders` for the count only.
- **N-03 (path separator).** `create path=` accepts forward slashes on Windows; the CLI normalises them into the vault path (filesystem shows `\` as expected). Backslashes would need shell quoting and are not probed; prefer `/` in `path=` values.

---

## 3. UX implication for `folderNew`

Because `create` silently materialises any missing prefix of the `path=` value:

1. The widget's `folderNew` flow **does not need a separate "create folder" step.** Any new folder typed by the user can be realised implicitly by the first `create` that places a note inside it. Adding a pre-flight "does this folder exist?" check against `obsidian folders` is redundant and introduces the stale-cache race noted in N-01.
2. Conversely, there is **no way to create an empty folder via the CLI** — folders only come into existence as a side effect of `create`-ing a file beneath them. If the UX ever needs "just make the folder", a placeholder note (or a filesystem-level `mkdir`) is the only option; the CLI surface does not expose it.
3. Typos in a deep folder path will produce a *new* folder tree silently (no disambiguation against existing folders). Consider validating user input against the live `folders` list before committing, or offering an autocomplete picker, to avoid accidental duplicate hierarchies (`audit-v3/` vs `Audit-v3/` — case, trailing spaces, etc., would each produce distinct trees).

---

## 4. Top-3 (priority order)

1. **[UX win] Drop any "ensure folder exists" pre-step from the `folderNew` flow.** A single `create path=a/b/c/new.md` call handles arbitrarily deep new hierarchies atomically and returns the authoritative `Created:` path. Added pre-flight logic would only re-introduce the stale-`folders` race.
2. **[UX guardrail] Validate new-folder input against the live `folders` list before committing,** to prevent silent creation of near-duplicate trees from typos / case / whitespace differences. (The CLI will happily create `audit-v3/` and `Audit-v3/` as two distinct folders.) An autocomplete or fuzzy-match confirm step closes this gap.
3. **[Documentary] Note the `folders` cache lag in any code that reads `folders` right after a deletion.** Either re-query after a short delay, or treat the filesystem as the source of truth when verifying post-delete state. No lag was observed after `create`, so this is strictly a deletion-path concern.

---

## Cleanup

- `obsidian delete path=audit-v3/deep/nested/new-note.md permanent` → `Deleted permanently: …` (exit=0)
- `Remove-Item -Recurse -Force` on `audit-v3/` — all three probe-created directories removed.
- Final `obsidian folders` (after settle): `/`, `Test` — **vault restored to pre-probe state.** Vault root contents: `.obsidian`, `Test`, `Welcome.md`.

## Caveats

- Obsidian version is unchanged since v1 (`1.12.7`); had it bumped, the full v1 probe matrix would need re-running before trusting any of these parsers.
- Only the nested-new-folder path was exercised for `create`; v2 already covered collision + `overwrite` + invalid-char paths, and those were not re-probed this run.
- The folder-list cache lag (N-01) was observed once; it is timing-dependent and not deterministically reproducible, so its exact duration is approximate.
