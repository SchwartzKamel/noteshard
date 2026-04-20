<#
.SYNOPSIS
    Signs the freshly-published MSIX using the PFX material provided via
    the SIGNING_PFX_BASE64 and SIGNING_PFX_PASSWORD environment variables.

.DESCRIPTION
    Called by .github/workflows/release.yml after dotnet publish produces
    an unsigned MSIX. The public counterpart of this cert
    (scripts/signing/noteshard-signing.cer) is committed to the repo and
    shipped alongside the MSIX so end users can trust it once before
    sideloading.

    Fails hard if either secret is missing, the PFX is corrupt, or
    signtool.exe can't be located. No fallback to a throwaway cert — we
    want users to be able to trust ONE thumbprint across all noteshard
    releases.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$pfxB64 = $env:SIGNING_PFX_BASE64
$pfxPwd = $env:SIGNING_PFX_PASSWORD

if ([string]::IsNullOrWhiteSpace($pfxB64)) {
    throw "SIGNING_PFX_BASE64 secret is not set. See scripts/bootstrap-signing-cert.ps1."
}
if ([string]::IsNullOrWhiteSpace($pfxPwd)) {
    throw "SIGNING_PFX_PASSWORD secret is not set."
}

$pfxPath = Join-Path $env:RUNNER_TEMP 'noteshard-signing.pfx'
[IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($pfxB64))

$repoRoot = Split-Path -Parent $PSScriptRoot
$msixSearchRoot = Join-Path $repoRoot 'src/ObsidianQuickNoteWidget/bin/x64/Release'
$msix = Get-ChildItem -Path $msixSearchRoot -Recurse -Filter '*.msix' -ErrorAction Stop | Select-Object -First 1
if (-not $msix) {
    throw "No .msix found under $msixSearchRoot. Run dotnet publish first."
}
Write-Host "Signing: $($msix.FullName)"

$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\' `
    -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue `
    | Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } `
    | Sort-Object FullName -Descending `
    | Select-Object -First 1
if (-not $signtool) {
    throw "signtool.exe not found. The windows-latest runner should have the Windows 10 SDK installed."
}

& $signtool.FullName sign `
    /fd SHA256 `
    /f $pfxPath `
    /p $pfxPwd `
    /tr 'http://timestamp.digicert.com' `
    /td SHA256 `
    $msix.FullName
if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }

Remove-Item $pfxPath -Force

Write-Host "Signed: $($msix.FullName)" -ForegroundColor Green
Write-Host "Release version: $Version"
