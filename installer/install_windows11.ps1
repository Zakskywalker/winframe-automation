# ==============================================================================
# WinFrame Automation 1.0 - Windows 11 Automated Installer
# Installs application, Start Menu & Desktop shortcuts, and registers with Windows 11
# Hand of Glory Games and Software | Zak Millikin (Zakskywalker M_A.P.)
# ==============================================================================

[CmdletBinding()]
param(
    [string]$InstallPath = "$env:LOCALAPPDATA\Programs\WinFrame",
    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " WinFrame Automation 1.0 - Windows 11 Setup" -ForegroundColor Yellow
Write-Host " Hand of Glory Games and Software | Zak Millikin" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$solutionRoot = Resolve-Path (Join-Path $scriptDir "..")
$publishDir = Join-Path $solutionRoot "publish\win11-x64"
$sourceExe = Join-Path $publishDir "WinFrame.App.exe"

if (-not (Test-Path $sourceExe)) {
    Write-Host "Published binary not found. Compiling Release build..." -ForegroundColor Yellow
    dotnet publish "$solutionRoot\src\WinFrame.App\WinFrame.App.csproj" -c Release -r win-x64 -o $publishDir
    dotnet publish "$solutionRoot\src\WinFrame.Cli\WinFrame.Cli.csproj" -c Release -r win-x64 -o $publishDir
    dotnet publish "$solutionRoot\src\WinFrame.Service\WinFrame.Service.csproj" -c Release -r win-x64 -o $publishDir
}

# 1. Create target installation directory & copy files
Write-Host "Installing to: $InstallPath" -ForegroundColor White
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
Copy-Item -Path "$publishDir\*" -Destination $InstallPath -Recurse -Force

$targetExe = Join-Path $InstallPath "WinFrame.App.exe"

# 2. Create Windows 11 Start Menu Shortcut
$startMenuDir = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs\WinFrame Automation")
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
$startMenuLnk = Join-Path $startMenuDir "WinFrame Automation.lnk"

$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($startMenuLnk)
$shortcut.TargetPath = $targetExe
$shortcut.WorkingDirectory = $InstallPath
$shortcut.Description = "WinFrame Automation Studio 1.0 - Hand of Glory Games and Software"
$shortcut.Save()
Write-Host "Created Start Menu shortcut: $startMenuLnk" -ForegroundColor Green

# 3. Create Desktop Shortcut if not suppressed
if (-not $NoDesktopShortcut) {
    $desktopDir = [System.Environment]::GetFolderPath("Desktop")
    $desktopLnk = Join-Path $desktopDir "WinFrame Automation.lnk"
    $dShortcut = $wshShell.CreateShortcut($desktopLnk)
    $dShortcut.TargetPath = $targetExe
    $dShortcut.WorkingDirectory = $InstallPath
    $dShortcut.Description = "WinFrame Automation Studio 1.0"
    $dShortcut.Save()
    Write-Host "Created Desktop shortcut: $desktopLnk" -ForegroundColor Green
}

# 4. Register with Windows 11 Installed Apps (Add/Remove Programs)
$uninstallRegKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WinFrameAutomation"
New-Item -Path $uninstallRegKey -Force | Out-Null
Set-ItemProperty -Path $uninstallRegKey -Name "DisplayName" -Value "WinFrame Automation Studio 1.0"
Set-ItemProperty -Path $uninstallRegKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $uninstallRegKey -Name "Publisher" -Value "Hand of Glory Games and Software (Zak Millikin)"
Set-ItemProperty -Path $uninstallRegKey -Name "InstallLocation" -Value $InstallPath
Set-ItemProperty -Path $uninstallRegKey -Name "DisplayIcon" -Value "$targetExe,0"
Set-ItemProperty -Path $uninstallRegKey -Name "NoModify" -Value 1
Set-ItemProperty -Path $uninstallRegKey -Name "NoRepair" -Value 1

Write-Host ""
Write-Host "SUCCESS: WinFrame Automation 1.0 has been installed for Windows 11!" -ForegroundColor Green

