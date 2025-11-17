param (
    [string]$Version = "0.0.1"
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

$BuildOutputDir = Join-Path $SolutionDir "build\Release" 
$StagingDir = Join-Path $SolutionDir "build\publish\win"
$DistDir = Join-Path $SolutionDir "build\installer"
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
if (Test-Path (Join-Path $SolutionDir "build")) { Remove-Item -Recurse -Force (Join-Path $SolutionDir "build") }
if (Test-Path $DistDir) { Remove-Item -Recurse -Force $DistDir }
New-Item -ItemType Directory -Force $StagingDir
New-Item -ItemType Directory -Force $DistDir

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
    $projectName = ($relativePath -split '\\')[0]
    
    if ($projectName -like "*.Tests") {
        Write-Host "Skipping test project: $projectName" -ForegroundColor Gray
        continue
    }

    if ($projectName.StartsWith("Axorith.Module.")) {
        Write-Host "Skipping module project (will be handled separately): $projectName" -ForegroundColor Gray
        continue
    }
    
    $destinationPath = Join-Path $StagingDir $projectName
    New-Item -ItemType Directory -Force $destinationPath
    
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

Write-Host "--- Patching Shim manifest ---" -ForegroundColor Cyan
$manifestPath = Join-Path $StagingDir "Axorith.Shim\axorith.json"
if (Test-Path $manifestPath) {
    $manifestContent = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $manifestContent.path = '$INSTDIR\Axorith.Shim\Axorith.Shim.exe'
    $manifestContent | ConvertTo-Json -Depth 5 | Set-Content $manifestPath -NoNewline
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