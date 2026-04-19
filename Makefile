# Makefile for ObsidianQuickNoteWidget
#
# Targets are self-documenting: add a double-hash comment after the target
# name and it will show up in `make help`.
#
# Usage: make <target>
#
# Works on Windows with GNU make (install via `winget install GnuWin32.Make`
# or `choco install make`) and either PowerShell or Git Bash as SHELL.

# ---- Configuration ---------------------------------------------------------

DOTNET       ?= dotnet
SOLUTION     ?= ObsidianQuickNoteWidget.slnx
CORE_TESTS   ?= tests/ObsidianQuickNoteWidget.Core.Tests/ObsidianQuickNoteWidget.Core.Tests.csproj
WIDGET_PROJ  ?= src/ObsidianQuickNoteWidget/ObsidianQuickNoteWidget.csproj
TRAY_PROJ    ?= src/ObsidianQuickNoteTray/ObsidianQuickNoteTray.csproj
CONFIG       ?= Debug
PLATFORM     ?= x64
ARTIFACTS    ?= artifacts

# If dotnet isn't on PATH but is at the default Windows install location,
# fall back to it automatically.
ifeq ($(OS),Windows_NT)
    ifeq ($(shell where $(DOTNET) 2>nul),)
        DOTNET := "C:/Program Files/dotnet/dotnet.exe"
    endif
endif

# Everything below runs through the shell; pick a sensible default.
ifeq ($(OS),Windows_NT)
    SHELL := cmd.exe
    .SHELLFLAGS := /C
endif

.DEFAULT_GOAL := help

# ---- Meta ------------------------------------------------------------------

.PHONY: help
ifeq ($(OS),Windows_NT)
help: ## Show this help (auto-generated from `##` comments on targets)
	@powershell -NoProfile -Command " \
	  Write-Host ''; \
	  Write-Host 'ObsidianQuickNoteWidget - available targets:' -ForegroundColor White; \
	  Write-Host ''; \
	  $$seen = @{}; \
	  Get-Content '$(firstword $(MAKEFILE_LIST))' | ForEach-Object { \
	    if ($$_ -match '^##@ (.+)$$') { Write-Host ''; Write-Host $$Matches[1] -ForegroundColor Yellow } \
	    elseif ($$_ -match '^([a-zA-Z0-9_.-]+):.*?## (.+)$$' -and -not $$seen.ContainsKey($$Matches[1])) { \
	      $$seen[$$Matches[1]] = $$true; \
	      '  {0,-18} {1}' -f $$Matches[1], $$Matches[2] | Write-Host -ForegroundColor Cyan } }; \
	  Write-Host ''; \
	  Write-Host 'Overrides: CONFIG=Release PLATFORM=x64 DOTNET=path/to/dotnet' -ForegroundColor DarkGray; \
	  Write-Host ''"
else
help: ## Show this help (auto-generated from `##` comments on targets)
	@echo ''
	@echo 'ObsidianQuickNoteWidget - available targets:'
	@echo ''
	@awk 'BEGIN {FS = ":.*?## "} \
	     /^[a-zA-Z0-9_.-]+:.*?## / { printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2 } \
	     /^##@ / { printf "\n\033[1m%s\033[0m\n", substr($$0, 5) }' \
	     $(MAKEFILE_LIST)
	@echo ''
	@echo 'Overrides: CONFIG=Release PLATFORM=x64 DOTNET=path/to/dotnet'
	@echo ''
endif

##@ Build

.PHONY: restore
restore: ## Restore NuGet packages for the solution
	$(DOTNET) restore $(SOLUTION)

.PHONY: build
build: ## Build the entire solution ($$CONFIG, default Debug)
	$(DOTNET) build $(SOLUTION) -c $(CONFIG) --nologo

.PHONY: rebuild
rebuild: clean build ## Clean then build

.PHONY: clean
clean: ## Remove bin/, obj/, and the artifacts directory
	$(DOTNET) clean $(SOLUTION) --nologo
	-@rmdir /S /Q $(ARTIFACTS) 2>nul || true

##@ Test

.PHONY: test
test: ## Run the Core xUnit test suite
	$(DOTNET) test $(CORE_TESTS) -c $(CONFIG) --nologo

.PHONY: test-fast
test-fast: ## Run tests without rebuilding (requires a prior `make build`)
	$(DOTNET) test $(CORE_TESTS) -c $(CONFIG) --nologo --no-build

.PHONY: test-watch
test-watch: ## Re-run Core tests on file change
	$(DOTNET) watch --project $(CORE_TESTS) test

##@ Run

.PHONY: run-tray
run-tray: ## Launch the tray companion app (global hotkey Ctrl+Alt+N)
	$(DOTNET) run --project $(TRAY_PROJ) -c $(CONFIG)

.PHONY: run-widget
run-widget: ## Launch the widget COM host directly (dev sanity check only)
	$(DOTNET) run --project $(WIDGET_PROJ) -c $(CONFIG)

##@ Packaging

.PHONY: pack
pack: ## Publish the widget as a sideload MSIX (Release, $$PLATFORM)
	$(DOTNET) publish $(WIDGET_PROJ) -c Release -p:Platform=$(PLATFORM) \
		-p:GenerateAppxPackageOnBuild=true \
		-p:AppxPackageSigningEnabled=false \
		-p:AppxBundle=Always \
		-p:UapAppxPackageBuildMode=SideloadOnly

.PHONY: pack-signed
pack-signed: ## Publish a signed MSIX (requires SIGNING_CERT + SIGNING_PASSWORD env vars)
ifeq ($(OS),Windows_NT)
	@powershell -NoProfile -Command " \
	  if (-not $$env:SIGNING_CERT)     { Write-Error 'SIGNING_CERT is required'; exit 1 }; \
	  if (-not $$env:SIGNING_PASSWORD) { Write-Error 'SIGNING_PASSWORD is required'; exit 1 }; \
	  $$resolved = (Resolve-Path -LiteralPath $$env:SIGNING_CERT -ErrorAction SilentlyContinue).Path; \
	  if (-not $$resolved) { $$resolved = $$env:SIGNING_CERT }; \
	  if ($$resolved -match '[\\\\/]dev-cert[\\\\/]') { \
	    Write-Error ('Refusing to use the local dev cert for a release build (SIGNING_CERT=' + $$resolved + '). See audit-reports/security-auditor.md F-01. Use tools\\Sign-DevMsix.ps1 for sideload dev signing instead.'); \
	    exit 1 \
	  }"
endif
	$(DOTNET) publish $(WIDGET_PROJ) -c Release -p:Platform=$(PLATFORM) \
		-p:GenerateAppxPackageOnBuild=true \
		-p:AppxPackageSigningEnabled=true \
		-p:PackageCertificateKeyFile=$(SIGNING_CERT) \
		-p:PackageCertificatePassword=$(SIGNING_PASSWORD) \
		-p:AppxBundle=Always \
		-p:UapAppxPackageBuildMode=StoreUpload

##@ Quality

.PHONY: format
format: ## Apply dotnet-format to the solution (non-destructive check via `format-check`)
	$(DOTNET) format $(SOLUTION)

.PHONY: format-check
format-check: ## Verify formatting without writing changes
	$(DOTNET) format $(SOLUTION) --verify-no-changes

.PHONY: ci
ci: restore build test ## One-shot: restore, build, test (what CI runs)

##@ Info

.PHONY: versions
versions: ## Show .NET SDK, solution, and git status summary
	@$(DOTNET) --version
	@$(DOTNET) --list-sdks
	@echo Solution: $(SOLUTION)  Config: $(CONFIG)  Platform: $(PLATFORM)
