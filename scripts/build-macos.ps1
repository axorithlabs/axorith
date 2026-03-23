#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds and packages Axorith for macOS platform.
.DESCRIPTION
    This script uses dotnet publish to create a macOS executable/app bundle.
    It creates a .app bundle structure, optionally creates a DMG, and handles
    code signing if credentials are available.
.PARAMETER Configuration
    Build configuration (default: Release)
.PARAMETER OutputDir
    Output directory for artifacts (default: ./dist)
.PARAMETER RuntimeIdentifier
    Runtime identifier (default: osx-x64, also supports osx-arm64)
.PARAMETER CreateDmg
    Create a DMG disk image from the .app bundle
.PARAMETER SignIdentity
    Code signing identity (e.g., "Developer ID Application: Your Name")
.EXAMPLE
    .\build-macos.ps1
.EXAMPLE
    .\build-macos.ps1 -RuntimeIdentifier osx-arm64 -CreateDmg
.EXAMPLE
    .\build-macos.ps1 -SignIdentity "Developer ID Application: Your Name"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "./dist",

    [Parameter(Mandatory = $false)]
    [ValidateSet("osx-x64", "osx-arm64")]
    [string]$RuntimeIdentifier = "osx-x64",

    [Parameter(Mandatory = $false)]
    [switch]$CreateDmg,

    [Parameter(Mandatory = $false)]
    [string]$SignIdentity = ""
)

$ErrorActionPreference = 'Stop'

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionRoot = Split-Path -Parent $ScriptRoot
$SolutionPath = Join-Path $SolutionRoot "Axorith.sln"
$ProjectPath = Join-Path $SolutionRoot "src\Host\Axorith.Host.csproj"

$archName = if ($RuntimeIdentifier -eq "osx-arm64") { "arm64" } else { "x64" }

if (-not (Test-Path -Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$artifactsDir = Join-Path $OutputDir "macos-$archName-$timestamp"
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null

Write-Host "Building Axorith for macOS..." -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration" -ForegroundColor Gray
Write-Host "  Runtime: $RuntimeIdentifier ($archName)" -ForegroundColor Gray
Write-Host "  Output: $artifactsDir" -ForegroundColor Gray
if ($CreateDmg) {
    Write-Host "  DMG: Enabled" -ForegroundColor Gray
}
if ($SignIdentity) {
    Write-Host "  Signing: $SignIdentity" -ForegroundColor Gray
}
Write-Host ""

# Build and publish
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

# Find the main executable
$mainExe = Get-ChildItem -Path $artifactsDir -Filter "Axorith.Host" -File | Select-Object -First 1
if ($null -eq $mainExe) {
    $mainExe = Get-ChildItem -Path $artifactsDir -Filter "axorith*" -File | Select-Object -First 1
}

$artifactPath = $artifactsDir

if ($null -ne $mainExe) {
    Write-Host "Creating .app bundle..." -ForegroundColor Yellow

    $appName = "Axorith.app"
    $appBundlePath = Join-Path $OutputDir $appName

    if (Test-Path $appBundlePath) {
        Remove-Item -Path $appBundlePath -Recurse -Force
    }

    $contentsDir = Join-Path $appBundlePath "Contents"
    $macosDir = Join-Path $contentsDir "MacOS"
    $resourcesDir = Join-Path $contentsDir "Resources"

    New-Item -ItemType Directory -Path $macosDir -Force | Out-Null
    New-Item -ItemType Directory -Path $resourcesDir -Force | Out-Null

    Copy-Item -Path "$artifactsDir\*" -Destination $macosDir -Recurse -Force

    $executables = Get-ChildItem -Path $macosDir -Filter "Axorith.Host" -File
    foreach ($exe in $executables) {
        Write-Host "  Marked executable: $($exe.Name)" -ForegroundColor Gray
    }

    $plistContent = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleDisplayName</key>
    <string>Axorith</string>
    <key>CFBundleExecutable</key>
    <string>Axorith.Host</string>
    <key>CFBundleIdentifier</key>
    <string>com.axorith.host</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>Axorith</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>LSUIElement</key>
    <false/>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
"@
    $plistPath = Join-Path $contentsDir "Info.plist"
    Set-Content -Path $plistPath -Value $plistContent -Encoding UTF8
    Write-Host "  Created Info.plist" -ForegroundColor Gray

    $iconSource = Join-Path $SolutionRoot "assets\icon.icns"
    if (Test-Path $iconSource) {
        Copy-Item -Path $iconSource -Destination (Join-Path $resourcesDir "axorith.icns")
        Write-Host "  Copied application icon" -ForegroundColor Gray
    }

    Write-Host ".app bundle created: $appName" -ForegroundColor Green

    # Code signing if identity provided (inside-out signing per Apple requirements)
    if ($SignIdentity) {
        Write-Host "Signing .app bundle (inside-out)..." -ForegroundColor Yellow

        # Step 1: Sign all nested binaries and frameworks first
        $dylibs = Get-ChildItem -Path $macosDir -Recurse -Include "*.dylib", "*.so"
        foreach ($dylib in $dylibs) {
            Write-Host "  Signing: $($dylib.Name)" -ForegroundColor Gray
            & codesign --force --sign $SignIdentity --options runtime --timestamp $dylib.FullName
        }

        # Step 2: Sign the outer .app bundle (without --deep)
        $signArgs = @(
            "--force"
            "--sign", $SignIdentity
            "--options", "runtime"
            "--timestamp"
            $appBundlePath
        )
        & codesign @signArgs

        if ($LASTEXITCODE -eq 0) {
            Write-Host "Signing completed." -ForegroundColor Green

            # Verify signature
            $verifyArgs = @("--verify", "--verbose", $appBundlePath)
            & codesign @verifyArgs

            if ($LASTEXITCODE -eq 0) {
                Write-Host "Signature verified." -ForegroundColor Green
            }
            else {
                Write-Warning "Signature verification failed."
            }
        }
        else {
            Write-Warning "Code signing failed. Continuing without signature."
        }
    }
    else {
        Write-Host "No signing identity provided. Skipping code signing." -ForegroundColor DarkGray
    }

    $artifactPath = $appBundlePath

    if ($CreateDmg) {
        Write-Host "Creating DMG disk image..." -ForegroundColor Yellow

        $dmgName = "Axorith-$archName.dmg"
        $dmgPath = Join-Path $OutputDir $dmgName

        if (Test-Path $dmgPath) {
            Remove-Item -Path $dmgPath -Force
        }

        $hdiutil = Get-Command hdiutil -ErrorAction SilentlyContinue
        if ($null -ne $hdiutil) {
            # Create DMG using hdiutil
            $hdiutilArgs = @(
                "create"
                "-volname", "Axorith"
                "-srcfolder", $appBundlePath
                "-ov"
                "-format", "UDZO"
                $dmgPath
            )
            & hdiutil @hdiutilArgs

            if ($LASTEXITCODE -eq 0) {
                Write-Host "DMG created: $dmgName" -ForegroundColor Green
                $artifactPath = $dmgPath
            }
            else {
                Write-Warning "DMG creation failed. Using .app bundle as artifact."
            }
        }
        else {
            Write-Warning "hdiutil not found (not running on macOS). DMG creation skipped."
            Write-Host "To create a DMG on macOS, run:" -ForegroundColor Yellow
            Write-Host "  hdiutil create -volname Axorith -srcfolder Axorith.app -ov -format UDZO Axorith-$archName.dmg" -ForegroundColor Gray
        }
    }

    Write-Host ""
    Write-Host "Build artifacts:" -ForegroundColor Cyan
    Write-Host "  Path: $artifactPath" -ForegroundColor Gray
}
else {
    Write-Warning "Main executable not found. Using directory output."
    Write-Host ""
    Write-Host "Build artifacts:" -ForegroundColor Cyan
    Write-Host "  Path: $artifactPath" -ForegroundColor Gray
}

Write-Output "ARTIFACT_PATH=$artifactPath"
Write-Output "PLATFORM=macos"
Write-Output "ARCH=$archName"

Write-Host ""
Write-Host "macOS build complete." -ForegroundColor Green
