<#
.SYNOPSIS
    Extracts the Keep-a-Changelog section for a given version and writes
    release notes to a file. If the section is not found, falls back to a
    minimal stub so the release publish step never hard-fails on missing
    notes (just warns in the log).

.PARAMETER Version
    The four-part version (e.g. "1.0.0.9"). Used to locate the
    "## [1.0.0.9]" section in CHANGELOG.md.

.PARAMETER OutputFile
    Where to write the extracted body.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$Version,
    [Parameter(Mandatory = $true)] [string]$OutputFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$changelog = Join-Path $repoRoot 'CHANGELOG.md'
$lines = Get-Content $changelog

$startPattern = "^##\s*\[$([Regex]::Escape($Version))\]"
$startIdx = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $startPattern) { $startIdx = $i; break }
}

if ($startIdx -lt 0) {
    $fallback = @(
        "# noteshard $Version",
        '',
        'See [CHANGELOG.md](../../blob/main/CHANGELOG.md) for release notes.',
        '',
        '## Install',
        '',
        '1. Download `noteshard-signing.cer` and import once:',
        '   `Import-Certificate -FilePath .\noteshard-signing.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople`',
        '2. Download the `.msix` and install:',
        '   `Add-AppxPackage -Path .\ObsidianQuickNoteWidget_*.msix -ForceApplicationShutdown`'
    )
    Set-Content -Path $OutputFile -Value $fallback
    Write-Warning "No CHANGELOG section for $Version found; wrote fallback release notes."
    return
}

$endIdx = $lines.Count - 1
for ($i = $startIdx + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s*\[') { $endIdx = $i - 1; break }
}

$body = $lines[($startIdx + 1)..$endIdx] -join [Environment]::NewLine
$install = @'

## Install

1. Download `noteshard-signing.cer` from this release and trust it once:
   ```powershell
   Import-Certificate -FilePath .\noteshard-signing.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```
2. Download the `.msix` and install:
   ```powershell
   Add-AppxPackage -Path .\ObsidianQuickNoteWidget_*.msix -ForceApplicationShutdown
   ```
3. Press `Win+W` and pin "Obsidian Quick Note".
'@

Set-Content -Path $OutputFile -Value ($body + $install)
Write-Host "Wrote release notes: $OutputFile"
