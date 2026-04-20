<!--
Thanks for opening a PR! Please fill out the sections below so CI and
reviewers have what they need. Delete any section that doesn't apply.
-->

## What

<!-- One-sentence summary of the change, in user-visible terms. -->

## Why

<!-- The problem this solves. Link issues with `Fixes #123` / `Refs #456`. -->

## How

<!-- Key design decisions. Anything surprising in the diff? Any
architectural impact on seams in ObsidianWidgetProvider or Core? -->

## Checklist

- [ ] `dotnet build -c Release` is clean (0W / 0E under `TreatWarningsAsErrors=true`)
- [ ] `dotnet test -c Release` passes
- [ ] If this changes provider behavior: BDD scenario added in `ObsidianWidgetProviderPushUpdateScenarios` (see [`docs/contributing/testing.md`](docs/contributing/testing.md#bdd-scenario-tests-widget-provider))
- [ ] If this is user-visible: `CHANGELOG.md [Unreleased]` updated
- [ ] If this is a release (tagged): `Package.appxmanifest` + three `winget/*.yaml` versions all agree (CI enforces this)
- [ ] Windows 11 only — no cross-platform assumptions introduced
- [ ] No secrets, PFX bytes, or personal paths committed (see `SECURITY.md`)

## Test plan

<!-- How you verified this locally. For widget changes, "pinned the MSIX
and did X in the Widget Board" is fine. -->
