<#
.SYNOPSIS
    Fails the build if the four version strings in the repo disagree. Called
    by CI on every PR/push, and by release.yml with -ExpectedVersion equal
    to the pushed tag (sans the leading "v").

.PARAMETER ExpectedVersion
    When provided, all four files must equal this value. When omitted, the
    four files just have to agree with one another.

.NOTES
    Files checked:
      - src/ObsidianQuickNoteWidget/Package.appxmanifest (<Identity Version="...">)
      - winget/ObsidianQuickNoteWidget.yaml (PackageVersion)
      - winget/ObsidianQuickNoteWidget.installer.yaml (PackageVersion)
      - winget/ObsidianQuickNoteWidget.locale.en-US.yaml (PackageVersion)
#>
[CmdletBinding()]
param(
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-ManifestVersion {
    $path = Join-Path $repoRoot 'src/ObsidianQuickNoteWidget/Package.appxmanifest'
    $xml = [xml](Get-Content -Path $path -Raw)
    return $xml.Package.Identity.Version
}

function Get-WingetVersion([string]$relative) {
    $path = Join-Path $repoRoot $relative
    $line = Select-String -Path $path -Pattern '^PackageVersion:\s*(\S+)' | Select-Object -First 1
    if (-not $line) { throw "PackageVersion not found in $relative" }
    return $line.Matches[0].Groups[1].Value
}

$versions = [ordered]@{
    'Package.appxmanifest'                              = Get-ManifestVersion
    'winget/ObsidianQuickNoteWidget.yaml'               = Get-WingetVersion 'winget/ObsidianQuickNoteWidget.yaml'
    'winget/ObsidianQuickNoteWidget.installer.yaml'     = Get-WingetVersion 'winget/ObsidianQuickNoteWidget.installer.yaml'
    'winget/ObsidianQuickNoteWidget.locale.en-US.yaml'  = Get-WingetVersion 'winget/ObsidianQuickNoteWidget.locale.en-US.yaml'
}

Write-Host "Version strings in repo:"
$versions.GetEnumerator() | ForEach-Object {
    Write-Host ("  {0,-48} {1}" -f $_.Key, $_.Value)
}

$distinct = @($versions.Values | Select-Object -Unique)
if ($distinct.Count -gt 1) {
    Write-Error "Version mismatch: $($distinct -join ', '). All four files must agree."
    exit 1
}

$canonical = $distinct[0]

if ($ExpectedVersion -and $canonical -ne $ExpectedVersion) {
    Write-Error "Tag version '$ExpectedVersion' does not match committed version '$canonical'. Bump the four files or retag."
    exit 1
}

Write-Host "All four files agree on $canonical." -ForegroundColor Green
