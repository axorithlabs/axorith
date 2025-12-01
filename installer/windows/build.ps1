param (
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

try {
    $PSScriptRoot = $MyInvocation.MyCommand.Path | Split-Path
    $SolutionDir = (Get-Item $PSScriptRoot).Parent.FullName | Split-Path
}
catch {
    Write-Error "Could not determine solution directory."
    exit 1
}

# Get version from git tag if not provided
if ([string]::IsNullOrWhiteSpace($Version)) {
    try {
        $gitVersion = & git describe --tags --abbrev=0 2>&1
        if ($LASTEXITCODE -eq 0 -and ![string]::IsNullOrWhiteSpace($gitVersion)) {
            $Version = $gitVersion.TrimStart('v').Trim()
            Write-Host "Version detected from git tag: $Version" -ForegroundColor Green
        }
        else {
            $Version = "0.0.1-alpha"
            Write-Host "No git tags found, using default version: $Version" -ForegroundColor Yellow
        }
    }
    catch {
        $Version = "0.0.1-alpha"
        Write-Host "Failed to get git version, using default: $Version" -ForegroundColor Yellow
    }
}
else {
    Write-Host "Using provided version: $Version" -ForegroundColor Cyan
}

$BuildOutputDir = Join-Path $SolutionDir "build\Release"
$StagingDir = Join-Path $SolutionDir "build\Publish\Windows"
$DistDir = Join-Path $SolutionDir "build\Installer"
$NsiScriptPath = Join-Path $PSScriptRoot "installer.nsi"

$NsisPath = Get-Command "makensis.exe" -ErrorAction SilentlyContinue
if (-not $NsisPath) {
    $NsisPath = Join-Path ${env:ProgramFiles(x86)} "NSIS\makensis.exe"
    if (-not (Test-Path $NsisPath)) {
        $NsisPath = Join-Path $env:ProgramFiles "NSIS\makensis.exe"
        if (-not (Test-Path $NsisPath)) {
            Write-Error "NSIS compiler (makensis.exe) not found."
            exit 1
        }
    }
}

Write-Host "--- Publishing the entire solution in Release mode ---" -ForegroundColor Cyan
if (Test-Path $DistDir) { Remove-Item -Recurse -Force $DistDir }
New-Item -ItemType Directory -Force $StagingDir | Out-Null
New-Item -ItemType Directory -Force $DistDir | Out-Null

dotnet publish $SolutionDir --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Solution publish failed."
    exit 1
}
Write-Host "Solution published successfully."

Write-Host "--- Aggregating published artifacts into staging directory ---" -ForegroundColor Cyan

$publishFolders = Get-ChildItem -Path $BuildOutputDir -Directory -Recurse -Filter "publish"

foreach ($folder in $publishFolders) {
    $relativePath = $folder.FullName.Substring($BuildOutputDir.Length + 1)
    $parts = $relativePath -split '\\'
    $topFolder = $parts[0]

    # Skip Tests folder
    if ($topFolder -eq "Tests") {
        Write-Host "Skipping test artifacts in: $relativePath" -ForegroundColor Gray
        continue
    }

    # Handle Modules
    if ($topFolder -eq "Modules") {
        continue
    }

    # Regular apps (e.g. Axorith.Host)
    $destinationPath = Join-Path $StagingDir $topFolder
    New-Item -ItemType Directory -Force $destinationPath | Out-Null

    Write-Host "Syncing '$($folder.FullName)' to '$destinationPath' using robocopy..."
    robocopy $folder.FullName $destinationPath /E /NFL /NDL /NJH /NJS /nc /ns /np
    if ($LASTEXITCODE -ge 8) {
        Write-Error "Robocopy failed with exit code $LASTEXITCODE"
        exit 1
    }
}

$sourceModulesPath = Join-Path $BuildOutputDir "modules"
$destModulesPath = Join-Path $StagingDir "modules"
if (Test-Path $sourceModulesPath) {
    Write-Host "Syncing modules to staging directory using robocopy..."
    robocopy $sourceModulesPath $destModulesPath /E /NFL /NDL /NJH /NJS /nc /ns /np
    if ($LASTEXITCODE -ge 8) {
        Write-Error "Robocopy failed for modules with exit code $LASTEXITCODE"
        exit 1
    }
}

Write-Host "--- Compiling the NSIS installer ---" -ForegroundColor Cyan
$nsisArgs = @(
    "/V2",
    "/DPRODUCT_VERSION=$Version",
    "/DBUILD_ROOT=$StagingDir"
)
$nsisArgs += $NsiScriptPath

$executablePath = if ($NsisPath.Source) { $NsisPath.Source } else { $NsisPath }
& $executablePath $nsisArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "NSIS compilation failed."
    exit 1
}

Write-Host "--- Build successful! ---" -ForegroundColor Green
$installerName = "Axorith-Setup-${Version}.exe"
$installerPath = Join-Path $DistDir $installerName
Write-Host "Installer created at: $installerPath"
