# Documentation

> **Platform:** Windows 11 22H2 or later. This is a Widget Board provider and has no macOS/Linux support.
>
> **Audience guide:** every doc under this tree declares its intended audience in a one-line framing at the top. Read the docs targeted at your role first.

This directory holds everything that didn't fit in the root [`README.md`](../README.md) — the audience-split user guide, the contributor handbook, and the onboarding material for AI coding agents working on the repo.

---

## Start here by role

### I want to **use** this widget

| Read | For |
| --- | --- |
| [`users/getting-started.md`](users/getting-started.md) | Install, first-run, pin the widget. |
| [`users/widgets/quick-note.md`](users/widgets/quick-note.md) | The main Quick Note widget — every field, every size. |
| [`users/widgets/recent-notes.md`](users/widgets/recent-notes.md) | The Recent Notes widget — how it refreshes, how to open a note. |
| [`users/widgets/plugin-runner.md`](users/widgets/plugin-runner.md) | The Plugin Runner widget — one-tap Obsidian commands. |
| [`users/tray-companion.md`](users/tray-companion.md) | The tray app + `Ctrl+Alt+N` global hotkey. |
| [`users/troubleshooting.md`](users/troubleshooting.md) | Copy-paste fixes for common issues. |

### I want to **contribute** (code, tests, releases)

| Read | For |
| --- | --- |
| [`contributing/development.md`](contributing/development.md) | Clone, build, test, sign, install locally. |
| [`contributing/architecture.md`](contributing/architecture.md) | The COM server, three widget definitions, state flow. |
| [`contributing/adaptive-cards.md`](contributing/adaptive-cards.md) | The Adaptive Cards 1.5 contract + card-input-id table. |
| [`contributing/cli-surface.md`](contributing/cli-surface.md) | Every `obsidian` CLI verb used, with verified stdout shape. |
| [`contributing/testing.md`](contributing/testing.md) | Test projects, fakes, how to run one. |
| [`contributing/release.md`](contributing/release.md) | Version bump, MSIX publish, winget pipeline. |
| [`contributing/security.md`](contributing/security.md) | Threat model and findings status. |

### I'm an **AI coding agent** working on this repo

| Read | For |
| --- | --- |
| [`agents/README.md`](agents/README.md) | 1-page onboarding, reading order, entry-point table. |
| [`agents/conventions.md`](agents/conventions.md) | The invariants and sharp edges that must not be broken. |
| [`agents/commands.md`](agents/commands.md) | Copy-paste commands for build / test / deploy. |
| [`../.github/copilot-instructions.md`](../.github/copilot-instructions.md) | The authoritative project brief for Copilot. |

### I'm a **security researcher** or **downstream maintainer**

| Read | For |
| --- | --- |
| [`../SECURITY.md`](../SECURITY.md) | Vulnerability reporting policy, scope, known considerations. |
| [`contributing/security.md`](contributing/security.md) | Threat model and per-file trust boundaries. |
| [`../CHANGELOG.md`](../CHANGELOG.md) | Release history and any security-related notes per version. |

---

## Top-level files worth knowing about

| File | What it is |
| --- | --- |
| [`../README.md`](../README.md) | The elevator-pitch landing page — links back here. |
| [`../CHANGELOG.md`](../CHANGELOG.md) | [Keep a Changelog](https://keepachangelog.com) format, per-release. |
| [`../SECURITY.md`](../SECURITY.md) | How to report vulnerabilities. |
| [`../LICENSE`](../LICENSE) | MIT. |
| [`../.github/copilot-instructions.md`](../.github/copilot-instructions.md) | Canonical brief for Copilot and other AI agents. Dense but load-bearing. |

---

## Conventions

- Each doc begins with a 1-sentence **audience statement**.
- Paths are relative to the doc's own location (so links work both on GitHub and in local editors).
- File references inside prose use `backticks` with the repo-relative path. Clickable links are used when we expect readers to actually follow them.
- If a doc disagrees with the code, the code wins — please open an issue.
