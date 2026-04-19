#Requires -Version 5.1
<#
.SYNOPSIS
  Signs a sideload MSIX with the locally generated dev cert.

.DESCRIPTION
  Reads the password written by `tools\New-DevCert.ps1` from the user-only
  `password.txt` and invokes signtool against the supplied MSIX. The password
  is never echoed. Refuses to run if the dev cert / password file is missing.

  NOTE: This script is for local sideload development ONLY. Release / store
  signing MUST use a genuine code-signing cert; see `make pack-signed`.

.PARAMETER Path
  Path to the .msix / .msixbundle to sign.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

$certDir = Join-Path $env:LocalAppData 'ObsidianQuickNoteWidget\dev-cert'
$pfxPath = Join-Path $certDir 'dev.pfx'
$pwdPath = Join-Path $certDir 'password.txt'

if (-not (Test-Path $pfxPath)) {
    throw "Dev cert not found at $pfxPath. Run .\tools\New-DevCert.ps1 first."
}
if (-not (Test-Path $pwdPath)) {
    throw "Dev cert password file not found at $pwdPath. Run .\tools\New-DevCert.ps1 first."
}
if (-not (Test-Path $Path)) {
    throw "MSIX not found: $Path"
}

$password = [System.IO.File]::ReadAllText($pwdPath).Trim()
try {
    & signtool sign /fd SHA256 /a /f $pfxPath /p $password $Path
    if ($LASTEXITCODE -ne 0) { throw "signtool exited with code $LASTEXITCODE" }
    Write-Host "Signed: $Path" -ForegroundColor Green
}
finally {
    $password = $null
    [System.GC]::Collect()
}
