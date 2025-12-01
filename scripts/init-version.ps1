#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Initializes the build version from git tags for the Axorith project.
    
.DESCRIPTION
    This script extracts the version from the latest git tag and logs it.
    Useful for CI/CD pipelines and local builds to ensure consistent versioning.
    
.EXAMPLE
    .\scripts\init-version.ps1
    
#>

param (
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Stop"

try {
    # Get the solution root directory
    $ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $SolutionRoot = Split-Path -Parent $ScriptRoot
    
    Push-Location $SolutionRoot
    
    # Try to get version from git tag
    $gitVersion = git describe --tags --abbrev=0 2>$null
    
    if ($LASTEXITCODE -eq 0 -and ![string]::IsNullOrWhiteSpace($gitVersion)) {
        # Remove 'v' prefix if present
        $cleanVersion = $gitVersion.TrimStart('v').Trim()
        Write-Host "✓ Version from git tag: $cleanVersion" -ForegroundColor Green
        
        if ($Verbose) {
            Write-Host "  Full tag: $gitVersion"
            $gitLog = git log -1 --format=%H
            Write-Host "  Commit: $gitLog"
            $tagDate = git log -1 --format=%aI $gitVersion
            Write-Host "  Tag date: $tagDate"
        }
        
        # Output the version for potential use by calling scripts
        Write-Output $cleanVersion
    }
    else {
        Write-Host "⚠ No git tags found, using default version 0.0.1-alpha" -ForegroundColor Yellow
        Write-Output "0.0.1-alpha"
    }
}
catch {
    Write-Host "✗ Error: $_" -ForegroundColor Red
    Write-Output "0.0.1-alpha"
    exit 1
}
finally {
    Pop-Location
}

