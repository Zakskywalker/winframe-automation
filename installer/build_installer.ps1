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

# 2. Publish Windows 11 win-x64 binaries
Write-Host "Publishing Windows 11 win-x64 binaries..." -ForegroundColor White
$publishDir = "$root\publish\win11-x64"
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
dotnet publish "$root\src\WinFrame.App\WinFrame.App.csproj" -c Release -r win-x64 -o $publishDir
dotnet publish "$root\src\WinFrame.Cli\WinFrame.Cli.csproj" -c Release -r win-x64 -o $publishDir
dotnet publish "$root\src\WinFrame.Service\WinFrame.Service.csproj" -c Release -r win-x64 -o $publishDir

Copy-Item "$root\LICENSE" "$publishDir\LICENSE" -Force
Copy-Item "$root\README.md" "$publishDir\README.md" -Force
Copy-Item "$scriptDir\install_windows11.ps1" "$publishDir\Install.ps1" -Force
New-Item -ItemType Directory -Force -Path "$publishDir\assets" | Out-Null
Copy-Item "$root\assets\splash.jpg" "$publishDir\assets\splash.jpg" -Force

# 3. Create Payload Zip for Self-Contained Installer
Write-Host "Creating WinFramePayload.zip..." -ForegroundColor White
$zipPath = "$root\installer\WinFramePayload.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

# 4. Publish Self-Contained Installer Wizard with Embedded Payload
Write-Host "Publishing self-contained WinFrame.Installer.exe with embedded payload..." -ForegroundColor White
$distDir = "$root\dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
dotnet publish "$root\src\WinFrame.Installer\WinFrame.Installer.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  "-p:PublishSingleFile=true" `
  "-p:IncludeNativeLibrariesForSelfExtract=true" `
  "-p:InstallerPayloadZip=$zipPath" `
  -o $distDir

Write-Host "Distribution bundle created at: $publishDir and $distDir\WinFrame.Installer.exe" -ForegroundColor Green
