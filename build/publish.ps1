# Builds the release installer: tests -> self-contained single-file publish -> Inno Setup.
# Output: dist\Time-to-stand-up-Setup-<version>.exe
# Usage:  powershell -ExecutionPolicy Bypass -File build\publish.ps1 [-SkipTests]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

[xml]$props = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Version not found in Directory.Build.props' }
Write-Host "StandUp version $version"

if (-not $SkipTests) {
    dotnet test tests\StandUp.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}

$publishDir = Join-Path $root 'publish'
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish src\StandUp.App -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    throw 'Inno Setup 6 (ISCC.exe) not found. Install it with: winget install JRSoftware.InnoSetup'
}

& $iscc "/DMyAppVersion=$version" "/DPublishDir=$publishDir" (Join-Path $root 'installer\StandUp.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compile failed' }

$setup = Join-Path $root "dist\Time-to-stand-up-Setup-$version.exe"
Write-Host "Installer: $setup"
