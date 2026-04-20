<#
.SYNOPSIS
    Applies branch-protection rules to `main` via the GitHub REST API.
    Requires the `gh` CLI authenticated as a repo admin.

.DESCRIPTION
    Turns on the PR-required workflow:
      - No direct pushes to `main` (admins included)
      - Every change goes through a Pull Request
      - CI "build-and-test" job must pass before merge
      - Branch must be up to date with base before merge
      - Stale reviews dismissed on new commits
      - Linear history (no merge commits)

    Run once after cloning, or any time you want to re-assert the rules.

.PARAMETER Owner
    Repository owner. Default: SchwartzKamel

.PARAMETER Repo
    Repository name. Default: noteshard
#>
[CmdletBinding()]
param(
    [string]$Owner = 'SchwartzKamel',
    [string]$Repo  = 'noteshard'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$payload = @{
    required_status_checks = @{
        strict   = $true
        contexts = @('build-and-test')
    }
    enforce_admins = $true
    required_pull_request_reviews = @{
        dismiss_stale_reviews           = $true
        require_code_owner_reviews      = $false
        required_approving_review_count = 0  # solo repo — bump to 1 when you add collaborators
    }
    restrictions           = $null
    required_linear_history = $true
    allow_force_pushes      = $false
    allow_deletions         = $false
    required_conversation_resolution = $true
} | ConvertTo-Json -Depth 10 -Compress

$tmp = New-TemporaryFile
$payload | Set-Content -Path $tmp -Encoding utf8

Write-Host "Applying branch protection to $Owner/$Repo on 'main'..."
gh api --method PUT `
    "repos/$Owner/$Repo/branches/main/protection" `
    --input $tmp `
    -H 'Accept: application/vnd.github+json'

Remove-Item $tmp -Force

Write-Host "Branch protection applied." -ForegroundColor Green
Write-Host "Verify at: https://github.com/$Owner/$Repo/settings/branches"
