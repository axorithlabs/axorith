#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generates release manifest with SHA256 verification for all platform artifacts.
.DESCRIPTION
    This script computes SHA256 hashes for each artifact and generates a JSON manifest
    following the schema defined in ROADMAP.md.
.PARAMETER Version
    The release version string (e.g., "1.3.0")
.PARAMETER Artifacts
    Array of paths to artifact files
.PARAMETER ReleaseNotes
    Release notes description
.PARAMETER OutputPath
    Output path for manifest.json (default: ./manifest.json)
.EXAMPLE
    .\generate-manifest.ps1 -Version "1.3.0" -Artifacts ".\dist\win-x64.exe",".\dist\linux-x64.AppImage",".\dist\macos-arm64.dmg"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [Parameter(Mandatory = $true, Position = 1)]
    [string[]]$Artifacts,

    [Parameter(Mandatory = $false, Position = 2)]
    [string]$ReleaseNotes = "",

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = "./manifest.json"
)

$ErrorActionPreference = 'Stop'

function Get-Sha256Hash {
    <#
    .SYNOPSIS
        Computes SHA256 hash for a file.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    if (-not (Test-Path -Path $FilePath -PathType Leaf)) {
        throw "Artifact not found: $FilePath"
    }

    $hash = Get-FileHash -Path $FilePath -Algorithm SHA256
    return $hash.Hash.ToLowerInvariant()
}

function Get-ArtifactInfo {
    <#
    .SYNOPSIS
        Determines platform, architecture, and type from file path.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    $fileName = [System.IO.Path]::GetFileName($FilePath)
    $extension = [System.IO.Path]::GetExtension($FilePath).ToLowerInvariant()

    $platform = "unknown"
    $arch = "unknown"
    $type = "unknown"

    # Determine platform and type from filename/extension
    if ($fileName -match "win" -or $extension -eq ".exe" -or $extension -eq ".msi") {
        $platform = "win"
        $type = if ($extension -eq ".msi") { "msi" } else { "exe" }
    }
    elseif ($fileName -match "linux" -or $extension -eq ".AppImage" -or $extension -eq ".appimage") {
        $platform = "linux"
        $type = "appimage"
    }
    elseif ($fileName -match "macos" -or $fileName -match "darwin" -or $extension -eq ".dmg" -or $extension -eq ".app") {
        $platform = "macos"
        $type = if ($extension -eq ".dmg") { "dmg" } else { "app" }
    }

    # Determine architecture
    if ($fileName -match "arm64" -or $fileName -match "aarch64") {
        $arch = "arm64"
    }
    elseif ($fileName -match "x64" -or $fileName -match "x86_64" -or $fileName -match "amd64") {
        $arch = "x64"
    }
    elseif ($fileName -match "x86" -or $fileName -match "i386" -or $fileName -match "i686") {
        $arch = "x86"
    }

    return @{
        Platform = $platform
        Arch = $arch
        Type = $type
    }
}

function New-Manifest {
    <#
    .SYNOPSIS
        Creates the release manifest JSON structure.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version,

        [Parameter(Mandatory = $true)]
        [hashtable[]]$Artifacts,

        [Parameter(Mandatory = $false)]
        [string]$ReleaseNotes
    )

    $manifest = @{
        version = $Version
        releaseNotes = $ReleaseNotes
        artifacts = $Artifacts
    }

    return $manifest
}

# Main execution
Write-Host "Generating release manifest for version: $Version" -ForegroundColor Cyan
Write-Host "Output path: $OutputPath" -ForegroundColor Cyan
Write-Host ""

$artifactList = @()

foreach ($artifactPath in $Artifacts) {
    Write-Host "Processing: $artifactPath" -ForegroundColor Yellow

    $sha256 = Get-Sha256Hash -FilePath $artifactPath
    Write-Host "  SHA256: $sha256" -ForegroundColor Gray

    $info = Get-ArtifactInfo -FilePath $artifactPath
    Write-Host "  Platform: $($info.Platform)" -ForegroundColor Gray
    Write-Host "  Architecture: $($info.Arch)" -ForegroundColor Gray
    Write-Host "  Type: $($info.Type)" -ForegroundColor Gray

    $artifactList += @{
        platform = $info.Platform
        arch = $info.Arch
        type = $info.Type
        url = $artifactPath
        sha256 = $sha256
    }

    Write-Host ""
}

$manifest = New-Manifest -Version $Version -Artifacts $artifactList -ReleaseNotes $ReleaseNotes

$json = $manifest | ConvertTo-Json -Depth 10
$json | Out-File -FilePath $OutputPath -Encoding UTF8

Write-Host "Manifest generated successfully: $OutputPath" -ForegroundColor Green
Write-Host ""
Write-Host "Manifest contents:" -ForegroundColor Cyan
Write-Host $json
