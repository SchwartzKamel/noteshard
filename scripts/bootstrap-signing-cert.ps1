<#
.SYNOPSIS
    One-time local setup. Generates the stable MSIX signing cert, exports
    the public .cer (commit this) and the private .pfx base64 (paste into
    the SIGNING_PFX_BASE64 GitHub secret). Prints the password to stdout
    for the SIGNING_PFX_PASSWORD secret.

.DESCRIPTION
    Run this ONCE to bootstrap CI signing. The generated cert Subject
    ("CN=ObsidianQuickNoteWidgetDev") must match Package.appxmanifest's
    <Identity Publisher="…"/> value exactly — the MSIX signing rules
    enforce this.

    After running, set the two repo secrets and commit the .cer:

      gh secret set SIGNING_PFX_BASE64 -b (Get-Content .\signing-pfx.b64 -Raw)
      gh secret set SIGNING_PFX_PASSWORD -b '<the password this script prints>'
      git add scripts/signing/noteshard-signing.cer
      git commit -m "chore(ci): add signing cert public half"

    Then delete .\signing-pfx.b64 locally. NEVER commit the PFX or its
    base64 — only the public .cer.

.NOTES
    The cert expires in 36 months. Re-run this script before expiry and
    rotate the secrets; existing installs remain valid because Windows
    trusts the cert by thumbprint for installation, not by expiry date.
#>
[CmdletBinding()]
param(
    [string]$Subject = 'CN=ObsidianQuickNoteWidgetDev',
    [string]$OutDir  = (Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts/signing')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

Write-Host "Generating self-signed code-signing cert: $Subject"
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -KeyUsage DigitalSignature `
    -FriendlyName 'noteshard MSIX signing' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddMonths(36) `
    -KeyExportPolicy Exportable

Write-Host "Thumbprint: $($cert.Thumbprint)"

# Generate a cryptographically strong password
Add-Type -AssemblyName System.Web
$password = [System.Web.Security.Membership]::GeneratePassword(32, 4)
$secure = ConvertTo-SecureString -String $password -Force -AsPlainText

$pfxPath = Join-Path $OutDir 'noteshard-signing.pfx'
$cerPath = Join-Path $OutDir 'noteshard-signing.cer'
$b64Path = Join-Path (Get-Location) 'signing-pfx.b64'

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $secure | Out-Null
Export-Certificate    -Cert $cert -FilePath $cerPath | Out-Null

$pfxBytes = [IO.File]::ReadAllBytes($pfxPath)
[IO.File]::WriteAllText($b64Path, [Convert]::ToBase64String($pfxBytes))

Remove-Item $pfxPath -Force

Write-Host ""
Write-Host "=== Bootstrap complete ===" -ForegroundColor Green
Write-Host "Public cert (commit this):"
Write-Host "  $cerPath"
Write-Host ""
Write-Host "PFX base64 (paste into SIGNING_PFX_BASE64 secret, then delete the file):"
Write-Host "  $b64Path"
Write-Host ""
Write-Host "Password (paste into SIGNING_PFX_PASSWORD secret):"
Write-Host "  $password" -ForegroundColor Yellow
Write-Host ""
Write-Host "Next:" -ForegroundColor Cyan
Write-Host "  gh secret set SIGNING_PFX_BASE64 --body (Get-Content '$b64Path' -Raw)"
Write-Host "  gh secret set SIGNING_PFX_PASSWORD --body '<paste password above>'"
Write-Host "  git add scripts/signing/noteshard-signing.cer"
Write-Host "  git commit -m 'chore(ci): add signing cert public half'"
Write-Host "  Remove-Item '$b64Path'  # IMPORTANT — do not commit"
