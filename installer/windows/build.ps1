param (
    [string]$Version = "",
    [string]$SigningCertificate = "",
    [string]$SigningPassword = "",
    [string]$NsisPluginDir = ""
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

$patchScript = Join-Path $SolutionDir "scripts\patch-telemetry-defaults.ps1"
if (-not [string]::IsNullOrWhiteSpace($env:POSTHOG_API_KEY) -and (Test-Path $patchScript)) {
    Write-Host "--- Patching desktop and website PostHog defaults for this build ---" -ForegroundColor Cyan
    & $patchScript -Key $env:POSTHOG_API_KEY -ApiHost $env:POSTHOG_API_HOST
}
elseif (-not (Test-Path $patchScript)) {
    Write-Warning "patch-telemetry-defaults.ps1 not found at $patchScript"
}

function Sign-Executable {
    param(
        [string]$FilePath,
        [string]$CertificatePath,
        [string]$Password
    )

    if (-not (Test-Path $FilePath)) {
        Write-Warning "File not found: $FilePath"
        return $false
    }

    $signtoolPath = $null
    $sdkPaths = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe",
        "${env:ProgramFiles}\Windows Kits\10\bin\*\x64\signtool.exe"
    )

    foreach ($path in $sdkPaths) {
        $found = Get-ChildItem -Path $path -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $signtoolPath = $found.FullName
            break
        }
    }

    if (-not $signtoolPath) {
        Write-Warning "signtool.exe not found. Skipping code signing for: $FilePath"
        Write-Host "To enable code signing, install Windows SDK or provide path to signtool.exe" -ForegroundColor Yellow
        return $false
    }

    try {
        $timestampServers = @(
            "http://timestamp.digicert.com",
            "http://timestamp.sectigo.com",
            "http://timestamp.globalsign.com/tsa/r6advanced1"
        )

        $timestampServer = $timestampServers[0]
        $signArgs = @(
            "sign",
            "/f", $CertificatePath,
            "/fd", "SHA256",
            "/tr", $timestampServer,
            "/td", "SHA256",
            "/d", "Axorith Digital Life System",
            "/du", "https://axorith.com"
        )

        if (-not [string]::IsNullOrWhiteSpace($Password)) {
            $signArgs += "/p", $Password
        }

        $signArgs += $FilePath

        Write-Host "Signing: $FilePath" -ForegroundColor Cyan
        & $signtoolPath $signArgs

        if ($LASTEXITCODE -eq 0) {
            Write-Host "Successfully signed: $FilePath" -ForegroundColor Green
            return $true
        }
        else {
            Write-Warning "Failed to sign: $FilePath (exit code: $LASTEXITCODE)"
            return $false
        }
    }
    catch {
        Write-Warning "Error signing $FilePath : $_"
        return $false
    }
}

# Version resolution: CLI param > git tag > fallback
# This ensures installer version matches the assembly version from Directory.Build.targets
if ([string]::IsNullOrWhiteSpace($Version)) {
    try {
        Push-Location $SolutionDir
        $gitVersion = & git describe --tags --abbrev=0 2>&1
        Pop-Location
        
        if ($LASTEXITCODE -eq 0 -and ![string]::IsNullOrWhiteSpace($gitVersion)) {
            $Version = $gitVersion.TrimStart('v').Trim()
            Write-Host "Version from git tag: $Version" -ForegroundColor Green
        }
        else {
            $Version = "0.0.1-alpha"
            Write-Host "No git tags found, using fallback: $Version" -ForegroundColor Yellow
        }
    }
    catch {
        $Version = "0.0.1-alpha"
        Write-Host "Failed to get git version, using fallback: $Version" -ForegroundColor Yellow
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

Write-Host "--- Publishing product projects in Release mode ---" -ForegroundColor Cyan
if (Test-Path $DistDir) { Remove-Item -Recurse -Force $DistDir }
if (Test-Path $StagingDir) { Remove-Item -Recurse -Force $StagingDir }
New-Item -ItemType Directory -Force $StagingDir | Out-Null
New-Item -ItemType Directory -Force $DistDir | Out-Null

$publishProjects = Get-ChildItem -Path (Join-Path $SolutionDir "src") -Filter "*.csproj" -File -Recurse
foreach ($project in $publishProjects) {
    Write-Host "Publishing $($project.FullName)..." -ForegroundColor Cyan
    & dotnet publish $project.FullName --configuration Release "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed for $($project.FullName)."
        exit 1
    }
}
Write-Host "Product projects published successfully."

Write-Host "--- Aggregating published artifacts into staging directory ---" -ForegroundColor Cyan

$publishFolders = Get-ChildItem -Path $BuildOutputDir -Directory -Recurse -Filter "publish"

foreach ($folder in $publishFolders) {
    $relativePath = $folder.FullName.Substring($BuildOutputDir.Length + 1)
    $parts = $relativePath -split '\\'
    $topFolder = $parts[0]

    if ($topFolder -eq "Tests") {
        Write-Host "Skipping test artifacts in: $relativePath" -ForegroundColor Gray
        continue
    }

    if ($topFolder -eq "Modules") {
        continue
    }

    $destinationPath = Join-Path $StagingDir $topFolder
    New-Item -ItemType Directory -Force $destinationPath | Out-Null

    Write-Host "Syncing '$($folder.FullName)' to '$destinationPath' using robocopy..."
    robocopy $folder.FullName $destinationPath /E /NFL /NDL /NJH /NJS /nc /ns /np
    if ($LASTEXITCODE -ge 8) {
        Write-Error "Robocopy failed with exit code $LASTEXITCODE"
        exit 1
    }
}

$sourceModulesPath = Join-Path $BuildOutputDir "Modules"
$destModulesPath = Join-Path $StagingDir "Modules"
if (Test-Path $sourceModulesPath) {
    Write-Host "Syncing modules to staging directory using robocopy..."
    robocopy $sourceModulesPath $destModulesPath /E /NFL /NDL /NJH /NJS /nc /ns /np
    if ($LASTEXITCODE -ge 8) {
        Write-Error "Robocopy failed for modules with exit code $LASTEXITCODE"
        exit 1
    }
}

if (-not [string]::IsNullOrWhiteSpace($SigningCertificate)) {
    if (-not (Test-Path $SigningCertificate)) {
        Write-Warning "Signing certificate not found at: $SigningCertificate"
        Write-Host "Skipping code signing. To sign executables, provide a valid certificate path." -ForegroundColor Yellow
    }
    else {
        Write-Host "--- Signing all executables ---" -ForegroundColor Cyan
        $exeFiles = Get-ChildItem -Path $StagingDir -Filter "*.exe" -Recurse -File
        $signedCount = 0
        foreach ($exe in $exeFiles) {
            if (Sign-Executable -FilePath $exe.FullName -CertificatePath $SigningCertificate -Password $SigningPassword) {
                $signedCount++
            }
        }
        Write-Host "Signed $signedCount of $($exeFiles.Count) executables" -ForegroundColor Green
    }
}
else {
    Write-Host "Code signing skipped (no certificate provided). Use -SigningCertificate parameter to enable." -ForegroundColor Yellow
}

Write-Host "--- Compiling the NSIS installer ---" -ForegroundColor Cyan
$nsisArgs = @(
    "/V2",
    "/DPRODUCT_VERSION=$Version",
    "/DBUILD_ROOT=$StagingDir"
)
if (-not [string]::IsNullOrWhiteSpace($env:POSTHOG_API_KEY)) {
    $posthogApiHost = $env:POSTHOG_API_HOST
    if ([string]::IsNullOrWhiteSpace($posthogApiHost)) {
        $posthogApiHost = "https://us.i.posthog.com"
    }
    $posthogApiHost = $posthogApiHost.TrimEnd('/')
    $nsisArgs += "/DPOSTHOG_API_KEY=$($env:POSTHOG_API_KEY)"
    $nsisArgs += "/DPOSTHOG_API_HOST=$posthogApiHost"
}
if (-not [string]::IsNullOrWhiteSpace($NsisPluginDir)) {
    if (-not (Test-Path -LiteralPath (Join-Path $NsisPluginDir "INetC.dll"))) {
        Write-Error "INetC.dll not found in NSIS plugin directory: $NsisPluginDir"
        exit 1
    }
    $nsisArgs += "/X!addplugindir `"$NsisPluginDir`""
}
$nsisArgs += $NsiScriptPath

$executablePath = if ($NsisPath.Source) { $NsisPath.Source } else { $NsisPath }
& $executablePath $nsisArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "NSIS compilation failed."
    exit 1
}

if (-not [string]::IsNullOrWhiteSpace($SigningCertificate) -and (Test-Path $SigningCertificate)) {
    $installerName = "axorith-setup.exe"
    $installerPath = Join-Path $DistDir $installerName
    if (Test-Path $installerPath) {
        Write-Host "--- Signing installer ---" -ForegroundColor Cyan
        Sign-Executable -FilePath $installerPath -CertificatePath $SigningCertificate -Password $SigningPassword | Out-Null
    }
}

Write-Host "--- Build successful! ---" -ForegroundColor Green
$installerName = "axorith-setup.exe"
$installerPath = Join-Path $DistDir $installerName
if (-not (Test-Path $installerPath)) {
    Write-Error "Installer output not found at $installerPath"
    exit 1
}
Write-Host "Installer created at: $installerPath"
