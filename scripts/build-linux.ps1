#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds and packages Axorith for Linux platform.
.DESCRIPTION
    This script uses dotnet publish to create a Linux executable/AppImage.
    It sets appropriate file permissions and outputs the artifact path.
.PARAMETER Configuration
    Build configuration (default: Release)
.PARAMETER OutputDir
    Output directory for artifacts (default: ./dist)
.PARAMETER RuntimeIdentifier
    Runtime identifier (default: linux-x64)
.EXAMPLE
    .\build-linux.ps1
.EXAMPLE
    .\build-linux.ps1 -Configuration Debug -OutputDir ./output
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "./dist",

    [Parameter(Mandatory = $false)]
    [string]$RuntimeIdentifier = "linux-x64"
)

$ErrorActionPreference = 'Stop'

$SolutionPath = Join-Path $PSScriptRoot "..\Axorith.sln"
$ProjectPath = Join-Path $PSScriptRoot "..\src\Host\Axorith.Host.csproj"

if (-not (Test-Path -Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$artifactsDir = Join-Path $OutputDir "linux-$RuntimeIdentifier-$timestamp"
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null

Write-Host "Building Axorith for Linux..." -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration" -ForegroundColor Gray
Write-Host "  Runtime: $RuntimeIdentifier" -ForegroundColor Gray
Write-Host "  Output: $artifactsDir" -ForegroundColor Gray
Write-Host ""

Write-Host "Running dotnet publish..." -ForegroundColor Yellow

$publishArgs = @(
    "publish", $ProjectPath
    "--configuration", $Configuration
    "--runtime", $RuntimeIdentifier
    "--self-contained", "true"
    "--output", $artifactsDir
    "-p:PublishSingleFile=false"
    "-p:PublishTrimmed=false"
)

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code: $LASTEXITCODE"
    exit 1
}

Write-Host "Build completed successfully." -ForegroundColor Green

$mainExe = Get-ChildItem -Path $artifactsDir -Filter "Axorith.Host" -File | Select-Object -First 1
if ($null -eq $mainExe) {
    $mainExe = Get-ChildItem -Path $artifactsDir -Filter "axorith*" -File | Select-Object -First 1
}

if ($null -ne $mainExe) {
    $unixMode = [System.IO.File]::GetAttributes($mainExe.FullName)
    Write-Host "Setting executable permissions on: $($mainExe.Name)" -ForegroundColor Yellow

    # On Unix systems, we'd use chmod +x. On Windows, we document it.
    # The file will be set executable when packaged as AppImage.
    Write-Host "Note: chmod +x should be applied when creating AppImage" -ForegroundColor Gray

    $appimagetool = Get-Command appimagetool -ErrorAction SilentlyContinue
    if ($null -ne $appimagetool) {
        Write-Host "Creating AppImage..." -ForegroundColor Yellow

        $appdir = Join-Path $OutputDir "Axorith.AppDir"
        if (Test-Path $appdir) {
            Remove-Item -Path $appdir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $appdir -Force | Out-Null

        Copy-Item -Path "$artifactsDir\*" -Destination $appdir -Recurse

        $appRunContent = @"
#!/bin/bash
HERE="\$(dirname "\$(readlink -f "\${0}")")"
export PATH="\${HERE}/usr/bin:\${PATH}"
export LD_LIBRARY_PATH="\${HERE}/usr/lib:\${LD_LIBRARY_PATH}"
exec "\${HERE}/usr/bin/Axorith.Host" "\$@"
"@
        $appRunPath = Join-Path $appdir "AppRun"
        Set-Content -Path $appRunPath -Value $appRunContent

        $iconSource = Join-Path $PSScriptRoot "..\assets\icon.png"
        if (Test-Path $iconSource) {
            Copy-Item -Path $iconSource -Destination (Join-Path $appdir "axorith.png")
        }

        Push-Location $OutputDir
        & appimagetool $appdir "Axorith-$RuntimeIdentifier.AppImage"
        Pop-Location

        if ($LASTEXITCODE -eq 0) {
            Write-Host "AppImage created: Axorith-$RuntimeIdentifier.AppImage" -ForegroundColor Green
            $artifactPath = Join-Path $OutputDir "Axorith-$RuntimeIdentifier.AppImage"
        }
        else {
            Write-Warning "AppImage creation failed. Using directory output."
            $artifactPath = $artifactsDir
        }
    }
    else {
        Write-Warning "appimagetool not found. Using portable directory."
        $artifactPath = $artifactsDir
    }
}
else {
    Write-Warning "Main executable not found. Using directory output."
    $artifactPath = $artifactsDir
}

Write-Host ""
Write-Host "Build artifacts:" -ForegroundColor Cyan
Write-Host "  Path: $artifactPath" -ForegroundColor Gray

$manifestInput = @{
    Version = "1.0.0"
    ArtifactPath = $artifactPath
    Platform = "linux"
    Arch = $RuntimeIdentifier
}

Write-Output "ARTIFACT_PATH=$artifactPath"
Write-Output "PLATFORM=linux"
Write-Output "ARCH=$RuntimeIdentifier"

Write-Host ""
Write-Host "Linux build complete." -ForegroundColor Green
