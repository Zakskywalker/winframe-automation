# ==============================================================================
# WinFrame Automation 1.0 - Build & Packaging Script
# Hand of Glory Games and Software | Zak Millikin (Zakskywalker M_A.P.)
# ==============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$root = Resolve-Path (Join-Path $scriptDir "..")

Write-Host "Building WinFrame Automation 1.0..." -ForegroundColor Cyan

# 1. Run Tests
Write-Host "Running xUnit tests..." -ForegroundColor White
dotnet test "$root\tests\WinFrame.Tests\WinFrame.Tests.csproj" -c Release

# 2. Publish Windows 11 win-x64 package
Write-Host "Publishing Windows 11 win-x64 package..." -ForegroundColor White
$publishDir = "$root\publish\win11-x64"
dotnet publish "$root\src\WinFrame.App\WinFrame.App.csproj" -c Release -r win-x64 -o $publishDir
dotnet publish "$root\src\WinFrame.Cli\WinFrame.Cli.csproj" -c Release -r win-x64 -o $publishDir
dotnet publish "$root\src\WinFrame.Service\WinFrame.Service.csproj" -c Release -r win-x64 -o $publishDir
dotnet publish "$root\src\WinFrame.Installer\WinFrame.Installer.csproj" -c Release -r win-x64 -o "$root\dist"

Copy-Item "$root\LICENSE" "$publishDir\LICENSE" -Force
Copy-Item "$root\README.md" "$publishDir\README.md" -Force
Copy-Item "$scriptDir\install_windows11.ps1" "$publishDir\Install.ps1" -Force

Write-Host "Distribution bundle created at: $publishDir and $root\dist\WinFrame.Installer.exe" -ForegroundColor Green
