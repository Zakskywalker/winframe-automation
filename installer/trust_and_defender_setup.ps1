<#
.SYNOPSIS
    Self-signs WinFrame binaries with a local Hand of Glory Authenticode certificate
    and optionally adds the workspace / install folder to Windows Defender exclusions.
    Run this script in an elevated PowerShell prompt if Windows Defender / SmartScreen flags unsigned local builds.
#>
param(
    [switch]$AddDefenderExclusion = $true,
    [switch]$SignBinaries = $true
)

$ErrorActionPreference = "Continue"
$repoRoot = Split-Path -Parent $PSScriptRoot
$installDir = Join-Path $env:LOCALAPPDATA "Programs\WinFrame"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " WinFrame Antivirus & Authenticode Trust Helper" -ForegroundColor Cyan
Write-Host " Hand of Glory Games and Software — Zak Millikin" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

if ($AddDefenderExclusion) {
    Write-Host "`n[1/2] Configuring Windows Defender Folder Exclusions..." -ForegroundColor Yellow
    try {
        Add-MpPreference -ExclusionPath $repoRoot -ErrorAction Stop
        if (Test-Path $installDir) {
            Add-MpPreference -ExclusionPath $installDir -ErrorAction SilentlyContinue
        }
        Write-Host "  Added Defender exclusion for: $repoRoot" -ForegroundColor Green
    }
    catch {
        Write-Host "  Note: Run PowerShell as Administrator to register Windows Defender exclusions (`Add-MpPreference`)." -ForegroundColor DarkYellow
    }
}

if ($SignBinaries) {
    Write-Host "`n[2/2] Creating/Locating Hand of Glory Local Code-Signing Certificate..." -ForegroundColor Yellow
    $subject = "CN=Hand of Glory Games and Software, O=Zak Millikin"
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq $subject } | Select-Object -First 1

    if (-not $cert) {
        $cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $subject `
            -KeyUsage DigitalSignature `
            -FriendlyName "Hand of Glory WinFrame Code Signing" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -NotAfter (Get-Date).AddYears(5)
        Write-Host "  Created code-signing certificate: $($cert.Thumbprint)" -ForegroundColor Green
    }

    $targets = Get-ChildItem -Path $repoRoot -Recurse -Include "WinFrame*.exe", "WinFrame*.dll" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\bin\\|\\publish\\|\\dist\\" }

    foreach ($file in $targets) {
        try {
            Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $cert -HashAlgorithm SHA256 | Out-Null
            Write-Host "  Signed: $($file.FullName)" -ForegroundColor Gray
        }
        catch { }
    }
}

Write-Host "`nDone!" -ForegroundColor Green
