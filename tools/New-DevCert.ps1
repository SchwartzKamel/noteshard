#Requires -Version 5.1
<#
.SYNOPSIS
  Generates a self-signed MSIX code-signing cert for local sideload development.

.DESCRIPTION
  Creates a fresh self-signed code-signing certificate (CN=ObsidianQuickNoteWidgetDev),
  exports it to `%LocalAppData%\ObsidianQuickNoteWidget\dev-cert\dev.pfx`, and
  writes a freshly generated random 24-character password to `password.txt` in
  the same folder. The password file's ACL is tightened to grant read access
  ONLY to the current user (plus SYSTEM + Administrators for recovery).

  The password is never printed to the console, committed to source, or shared.
  To sign an MSIX, use `tools\Sign-DevMsix.ps1 <path-to-msix>` which reads
  password.txt at runtime.

  Security rationale: see `audit-reports\security-auditor.md` F-01
  (CWE-798 — the dev-cert password was previously a hardcoded literal).

.PARAMETER Force
  Overwrite an existing dev.pfx / password.txt without prompting.

.EXAMPLE
  .\tools\New-DevCert.ps1
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$certDir = Join-Path $env:LocalAppData 'ObsidianQuickNoteWidget\dev-cert'
$pfxPath = Join-Path $certDir 'dev.pfx'
$cerPath = Join-Path $certDir 'dev.cer'
$pwdPath = Join-Path $certDir 'password.txt'

if (-not (Test-Path $certDir)) {
    New-Item -ItemType Directory -Path $certDir -Force | Out-Null
}

if ((Test-Path $pfxPath) -and -not $Force) {
    Write-Host "Dev cert already exists at $pfxPath. Re-run with -Force to rotate." -ForegroundColor Yellow
    return
}

# --- Generate a 24-char random password ---------------------------------------
# Use a URL/shell-safe alphabet so it round-trips through Makefile / CI env vars.
$alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'.ToCharArray()
$bytes = New-Object byte[] 24
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$sb = New-Object System.Text.StringBuilder
for ($i = 0; $i -lt 24; $i++) {
    [void]$sb.Append($alphabet[$bytes[$i] % $alphabet.Length])
}
$password = $sb.ToString()
$secure = ConvertTo-SecureString -String $password -AsPlainText -Force

# --- Create and export cert ----------------------------------------------------
$cert = New-SelfSignedCertificate `
    -Subject 'CN=ObsidianQuickNoteWidgetDev' `
    -Type CodeSigningCert `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddDays(90) `
    -FriendlyName 'ObsidianQuickNoteWidget Dev (generated)'

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $secure -Force | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null

# --- Write password.txt with user-only ACL ------------------------------------
# Order matters: create the file first with restrictive ACL, THEN write the secret.
if (Test-Path $pwdPath) { Remove-Item $pwdPath -Force }
New-Item -ItemType File -Path $pwdPath -Force | Out-Null

try {
    $acl = New-Object System.Security.AccessControl.FileSecurity
    $acl.SetAccessRuleProtection($true, $false)  # disable inheritance, drop inherited rules

    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $rules = @(
        [System.Security.AccessControl.FileSystemAccessRule]::new(
            $currentUser,
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            [System.Security.AccessControl.AccessControlType]::Allow),
        [System.Security.AccessControl.FileSystemAccessRule]::new(
            (New-Object System.Security.Principal.SecurityIdentifier 'S-1-5-18'),  # SYSTEM
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            [System.Security.AccessControl.AccessControlType]::Allow),
        [System.Security.AccessControl.FileSystemAccessRule]::new(
            (New-Object System.Security.Principal.SecurityIdentifier 'S-1-5-32-544'),  # Administrators
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            [System.Security.AccessControl.AccessControlType]::Allow)
    )
    foreach ($r in $rules) { $acl.AddAccessRule($r) }
    $acl.SetOwner($currentUser)
    Set-Acl -Path $pwdPath -AclObject $acl
}
catch {
    Write-Warning "Failed to tighten ACL on $pwdPath ($($_.Exception.Message)). The password file was still created but may be broadly readable."
}

# UTF-8 without BOM. Use -NoNewline to avoid trailing CRLF confusing callers.
[System.IO.File]::WriteAllText($pwdPath, $password, (New-Object System.Text.UTF8Encoding $false))

# Clear the in-memory secret variables.
$password = $null
$sb = $null
[System.GC]::Collect()

Write-Host "Dev cert generated:" -ForegroundColor Green
Write-Host "  PFX      : $pfxPath"
Write-Host "  CER      : $cerPath"
Write-Host "  Password : $pwdPath (user-only ACL)"
Write-Host ""
Write-Host "The password is NOT printed. Use tools\Sign-DevMsix.ps1 <msix> to sign," -ForegroundColor Cyan
Write-Host "or read it programmatically from `$env:LocalAppData\ObsidianQuickNoteWidget\dev-cert\password.txt."
Write-Host ""
Write-Host "One-time: import $cerPath into Trusted People (LocalMachine) before installing the signed MSIX."
