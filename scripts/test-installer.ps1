param(
    [string]$InstallerPath = (Join-Path $PSScriptRoot '../build/Installer/axorith-setup.exe'),
    [string]$PreviousInstallerPath = '',
    [switch]$RequireCleanRuntime
)
$ErrorActionPreference = 'Stop'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$disposable = ($env:GITHUB_ACTIONS -eq 'true' -and $env:RUNNER_ENVIRONMENT -eq 'github-hosted') -or
    ($env:USERNAME -eq 'WDAGUtilityAccount')
if (-not $disposable) { throw 'Installer E2E requires Windows Sandbox or a GitHub-hosted disposable runner. It must not run on a development machine.' }
$githubHeaders = @{ Accept = 'application/vnd.github+json' }
if ($env:GITHUB_TOKEN) {
    $githubHeaders.Authorization = 'Bearer ' + $env:GITHUB_TOKEN
    Remove-Item Env:GITHUB_TOKEN
}
$env:AXORITH_DEV = '1'
$env:AXORITH_TEST_MODE = '1'
$env:AXORITH_TELEMETRY_DISABLED = '1'
$env:AXORITH_TELEMETRY_API_KEY = '##E2E_DISABLED##'
$env:AXORITH_TELEMETRY_HOST = 'http://127.0.0.1:9'
# Older desktop builds read only the user environment; this machine is disposable.
[Environment]::SetEnvironmentVariable('AXORITH_TELEMETRY_API_KEY', $env:AXORITH_TELEMETRY_API_KEY, 'User')
# Sandbox can forward DNS to the host, bypassing the guest's hosts file.
if ($env:USERNAME -eq 'WDAGUtilityAccount') {
    New-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Containers' -Name SendDnsToHost -Value 0 -PropertyType DWord -Force | Out-Null
    $dnsService = Get-Service Dnscache
    # Dnscache denies SERVICE_STOP even to the Sandbox administrator.
    if ($dnsService.Status -eq 'Running') {
        & "$env:WINDIR\System32\taskkill.exe" /F /FI 'SERVICES eq Dnscache'
        if ($LASTEXITCODE -ne 0) { throw 'Could not restart the disposable Sandbox DNS resolver.' }
        $dnsService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(10))
    }
    Start-Service Dnscache
    (Get-Service Dnscache).WaitForStatus('Running', [TimeSpan]::FromSeconds(10))
}
# Old installers have compiled-in endpoints and cannot understand the new flag.
$posthogHosts = @('us.i.posthog.com', 'us.posthog.com', 'eu.i.posthog.com', 'eu.posthog.com', 'app.posthog.com', 'i.posthog.com')
$env:NO_PROXY = (($env:NO_PROXY, ($posthogHosts -join ',')) | Where-Object { $_ }) -join ','
$hostsPath = Join-Path $env:WINDIR 'System32/drivers/etc/hosts'
[IO.File]::AppendAllLines($hostsPath, [string[]](@('') + @($posthogHosts | ForEach-Object { "127.0.0.1 $_"; "::1 $_" })))
[IO.File]::AppendAllLines($hostsPath, [string[]]@('127.0.0.1 blocked.axorith.test', '127.0.0.1 allowed.axorith.test'))
Clear-DnsClientCache
$systemProxy = [Net.WebRequest]::GetSystemWebProxy()
foreach ($hostName in $posthogHosts) {
    $addresses = [Net.Dns]::GetHostAddresses($hostName)
    $proxyBypassed = $systemProxy.IsBypassed([uri]"https://$hostName/capture")
    if ($addresses.Count -eq 0 -or @($addresses | Where-Object { -not [Net.IPAddress]::IsLoopback($_) }).Count -ne 0 -or -not $proxyBypassed) {
        throw "PostHog isolation could not be verified for $hostName (addresses: $($addresses -join ', '); proxy bypass: $proxyBypassed); refusing to start installer E2E."
    }
}
Write-Host 'Installer E2E: PostHog disabled; legacy key invalidated and capture hosts isolated to loopback.'
if (-not [Environment]::Is64BitOperatingSystem) { throw 'The product installer requires x64 Windows.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$results = Join-Path $root 'build/Release'
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$installRoot = Join-Path $env:ProgramFiles 'Axorith'
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Axorith'
$legacyKey = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Axorith'
$expected = [Diagnostics.FileVersionInfo]::GetVersionInfo($installer).ProductVersion
$owned = [Collections.Generic.List[Diagnostics.Process]]::new()
New-Item -ItemType Directory -Force $results | Out-Null
if (Test-Path -LiteralPath $installRoot) { throw 'Expected an empty machine; an Axorith installation already exists.' }
$preferenceDirectory = Join-Path $env:APPDATA 'Axorith/config'
New-Item -ItemType Directory -Force $preferenceDirectory | Out-Null
[IO.File]::WriteAllText((Join-Path $preferenceDirectory 'telemetry-preference.txt'), 'false')

function Assert-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Invoke-Setup([string]$Path, [string]$Arguments = '/S', [int]$ExpectedExit = 0, [int]$TimeoutSeconds = 600) {
    Write-Host "Running $(Split-Path -Leaf $Path); expected exit $ExpectedExit."
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    $owned.Add($process)
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Write-Host "Timed out process $($process.Id): window='$($process.MainWindowTitle)', AXORITH_DEV=$env:AXORITH_DEV"
        Write-Host "Maintenance Host present: $(Test-Path (Join-Path $installRoot 'Axorith.Host/Axorith.Host.exe')); protected commitment root present: $(Test-Path (Join-Path $env:ProgramData 'Axorith/CommittedSessions'))"
        throw "Installer timed out: $Path"
    }
    Assert-Condition ($process.ExitCode -eq $ExpectedExit) "Installer exit code $($process.ExitCode), expected $ExpectedExit : $Path"
}
function Assert-Installed {
    foreach ($product in @('Axorith.Host', 'Axorith.Client', 'Axorith.Shim')) {
        $assembly = Join-Path $installRoot "$product/$product.dll"
        Assert-Condition (Test-Path -LiteralPath $assembly) "Missing installed assembly: $assembly"
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($assembly).ProductVersion.Split('+')[0]
        Assert-Condition ($version -eq $expected) "Wrong installed version: $product=$version, installer=$expected"
    }
    $registration = Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue
    if (-not $registration) { $registration = Get-ItemProperty $legacyKey -ErrorAction Stop }
    Assert-Condition ($registration.DisplayVersion -eq $expected) 'Uninstall registration has the wrong version.'
    $shortcut = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs/Axorith.lnk'
    Assert-Condition (Test-Path -LiteralPath $shortcut) 'Start menu shortcut is missing.'
    $shell = New-Object -ComObject WScript.Shell
    Assert-Condition ($shell.CreateShortcut($shortcut).TargetPath -eq (Join-Path $installRoot 'Axorith.Client/Axorith.Client.exe')) 'Shortcut points to a different executable.'
}
function Set-GuestAspNetRuntimeUnavailable {
    # Leave the real installer registration present to exercise repair of damaged runtime files.
    foreach ($process in @(Get-Process Axorith.Client,Axorith.Host,Axorith.Shim -ErrorAction SilentlyContinue)) {
        try {
            if (-not $process.HasExited -and $process.Path -and
                $process.Path.StartsWith($installRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $process.Id -Force
                Assert-Condition ($process.WaitForExit(10000)) 'Installed test process did not stop before runtime fault injection.'
            }
        }
        finally { $process.Dispose() }
    }
    $frameworkRoot = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'dotnet/shared/Microsoft.AspNetCore.App'))
    $frameworkDirectories = @(Get-ChildItem -LiteralPath $frameworkRoot -Directory | Where-Object Name -Match '^10\.\d+\.\d+$')
    Assert-Condition ($frameworkDirectories.Count -gt 0) 'The clean-image install did not supply an ASP.NET 10 runtime.'
    foreach ($directory in $frameworkDirectories) {
        $source = [IO.Path]::GetFullPath($directory.FullName)
        $suffix = [Guid]::NewGuid().ToString('N')
        $destination = [IO.Path]::GetFullPath((Join-Path $frameworkRoot ('e2e-disabled-' + $directory.Name + '-' + $suffix)))
        Assert-Condition ($source.StartsWith($frameworkRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and
            $destination.StartsWith($frameworkRoot + '\', [StringComparison]::OrdinalIgnoreCase)) 'Runtime fault-injection path escaped its explicit guest framework directory.'
        Move-Item -LiteralPath $source -Destination $destination
    }
    $remainingRuntimes = & "$env:ProgramFiles\dotnet\dotnet.exe" --list-runtimes
    Assert-Condition (-not ($remainingRuntimes -match '^Microsoft\.AspNetCore\.App 10\.')) 'Runtime fault injection left a selectable ASP.NET 10 framework.'
}
function Test-ClientStartup([string]$ProductRoot) {
    $client = Start-Process -FilePath (Join-Path $ProductRoot 'Axorith.Client/Axorith.Client.exe') -PassThru -WindowStyle Hidden
    $owned.Add($client)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 30 -and -not $client.HasExited -and $client.MainWindowHandle -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 100
        $client.Refresh()
    }
    Assert-Condition (-not $client.HasExited) 'Installed Client exited during startup.'
    Assert-Condition ($client.MainWindowHandle -ne [IntPtr]::Zero) 'Installed Client did not create a native window.'
}
function Test-InstalledApis([string]$Phase) {
    $env:AXORITH_E2E_INSTALL_ROOT = $installRoot
    if (-not $env:AXORITH_E2E_BROWSER_EXE) {
        $versions = Invoke-RestMethod 'https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json'
        $downloads = @($versions.channels.Stable.downloads.chrome | Where-Object platform -eq 'win64')
        Assert-Condition ($downloads.Count -eq 1) 'Chrome for Testing has no unique stable Windows x64 download.'
        $download = [uri]$downloads[0].url
        Assert-Condition ($download.Scheme -eq 'https' -and $download.Host -eq 'storage.googleapis.com') 'Unexpected Chrome for Testing download origin.'
        $browserVersion = [version]$versions.channels.Stable.version
        $archive = Join-Path $results "chrome-$browserVersion-win64.zip"
        if (-not (Test-Path -LiteralPath $archive)) {
            Invoke-WebRequest $download.AbsoluteUri -OutFile $archive -UseBasicParsing
        }
        # Configure Chrome's AppContainer permissions on guest-local files, as its installer does.
        $browserDirectory = Join-Path $env:TEMP 'AxorithChromeForTesting'
        Expand-Archive -LiteralPath $archive -DestinationPath $browserDirectory -Force
        $env:AXORITH_E2E_BROWSER_EXE = Join-Path $browserDirectory 'chrome-win64/chrome.exe'
        $chromeDirectory = Split-Path -Parent $env:AXORITH_E2E_BROWSER_EXE
        # Chromium defines CONFIGURE_APP_CONTAINER_SANDBOX_SUCCESS as exit code 78.
        Invoke-Setup (Join-Path $chromeDirectory 'setup.exe') ('--configure-browser-in-directory="' + $chromeDirectory + '"') 78
    }
    Assert-Condition (Test-Path -LiteralPath $env:AXORITH_E2E_BROWSER_EXE) 'Chrome for Testing executable is missing.'
    if (-not $env:AXORITH_E2E_EDGE_EXE) {
        $env:AXORITH_E2E_EDGE_EXE = @(
            (Join-Path $env:ProgramFiles 'Microsoft/Edge/Application/msedge.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Microsoft/Edge/Application/msedge.exe')) |
            Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    Assert-Condition (-not [string]::IsNullOrEmpty($env:AXORITH_E2E_EDGE_EXE)) 'Microsoft Edge is unavailable in this disposable Windows image.'
    Write-Host "Edge version: $([Diagnostics.FileVersionInfo]::GetVersionInfo($env:AXORITH_E2E_EDGE_EXE).ProductVersion)"
    if (-not $env:AXORITH_E2E_FIREFOX_EXE) {
        $env:AXORITH_E2E_FIREFOX_EXE = Join-Path $env:ProgramFiles 'Mozilla Firefox/firefox.exe'
        if (-not (Test-Path -LiteralPath $env:AXORITH_E2E_FIREFOX_EXE)) {
            $firefoxInstaller = Join-Path $results 'firefox-setup.exe'
            if (-not (Test-Path -LiteralPath $firefoxInstaller)) {
                Invoke-WebRequest 'https://download.mozilla.org/?product=firefox-latest-ssl&os=win64&lang=en-US' -OutFile $firefoxInstaller -UseBasicParsing
            }
            Invoke-Setup $firefoxInstaller '/S /DesktopShortcut=false /TaskbarShortcut=false /MaintenanceService=false'
        }
    }
    if (-not $env:AXORITH_E2E_GECKODRIVER_EXE) {
        $geckoVersion = '0.37.1'
        $geckoArchive = Join-Path $results "geckodriver-v$geckoVersion-win64.zip"
        if (-not (Test-Path -LiteralPath $geckoArchive)) {
            Invoke-WebRequest "https://github.com/mozilla/geckodriver/releases/download/v$geckoVersion/geckodriver-v$geckoVersion-win64.zip" -OutFile $geckoArchive -UseBasicParsing
        }
        $geckoDirectory = Join-Path $env:TEMP 'AxorithGeckoDriver'
        Expand-Archive -LiteralPath $geckoArchive -DestinationPath $geckoDirectory -Force
        $env:AXORITH_E2E_GECKODRIVER_EXE = Join-Path $geckoDirectory 'geckodriver.exe'
    }
    Assert-Condition ((Test-Path -LiteralPath $env:AXORITH_E2E_FIREFOX_EXE) -and (Test-Path -LiteralPath $env:AXORITH_E2E_GECKODRIVER_EXE)) 'Firefox or geckodriver is unavailable.'
    Write-Host "Firefox version: $([Diagnostics.FileVersionInfo]::GetVersionInfo($env:AXORITH_E2E_FIREFOX_EXE).ProductVersion)"
    $filter = 'FullyQualifiedName~HostProcessEndToEndTests|FullyQualifiedName~ShimProcessTests|FullyQualifiedName~NativeWindowExecutionTests'
    Invoke-InstalledTests $filter "installed-product-$Phase.trx" 28
}
function Invoke-InstalledTests([string]$Filter, [string]$TrxName, [int]$ExpectedCount) {
    $dll = Join-Path $results 'Axorith.Integrations.Tests/Axorith.Integrations.Tests.dll'
    $trxPath = Join-Path $results $trxName
    if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath }
    Write-Host "Running installed-product scenarios: $TrxName."
    $previousErrorPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 treats native stderr as a terminating error under Stop.
        $ErrorActionPreference = 'Continue'
        if ($env:AXORITH_VSTEST_PATH) {
            & "$env:ProgramFiles\dotnet\dotnet.exe" exec --runtimeconfig ([IO.Path]::ChangeExtension($dll, 'runtimeconfig.json')) $env:AXORITH_VSTEST_PATH $dll "/TestCaseFilter:$filter" `
                "/ResultsDirectory:$results" "/Logger:trx;LogFileName=$trxName" `
                '--' 'RunConfiguration.TreatNoTestsAsError=true'
        }
        else {
            & "$env:ProgramFiles\dotnet\dotnet.exe" test $dll --filter $filter `
                --results-directory $results --logger "trx;LogFileName=$trxName" -- RunConfiguration.TreatNoTestsAsError=true
        }
        $testExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorPreference }
    if ($testExit -ne 0) { throw 'The installed product failed its real Client API / process / native-messaging tests.' }
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $skipped = @($trx.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'NotExecuted').Count
    Assert-Condition ([int]$counters.executed -eq $ExpectedCount -and [int]$counters.passed -eq $ExpectedCount -and $skipped -eq 0) "Expected all $ExpectedCount installed-product scenarios in $TrxName to execute and pass."
}
function Assert-NativeRegistrationRemoved {
    foreach ($browserKey in @('Mozilla', 'Google\Chrome', 'Chromium', 'Microsoft\Edge')) {
        foreach ($hostName in @('axorith', 'axorith.dev')) {
            Assert-Condition (-not (Test-Path "HKCU:\Software\$browserKey\NativeMessagingHosts\$hostName")) "Native messaging registration remains: $browserKey/$hostName"
        }
    }
}

function Test-IsolatedWindowsPolicies {
    $dll = Join-Path $results 'Axorith.Integrations.Tests/Axorith.Integrations.Tests.dll'
    $vstest = $env:AXORITH_VSTEST_PATH
    if (-not $vstest) {
        $sdk = (& dotnet --list-sdks | Select-Object -Last 1) -split ' ', 2
        $vstest = Join-Path (Join-Path $sdk[1].Trim('[', ']') $sdk[0]) 'vstest.console.dll'
    }
    Assert-Condition (Test-Path -LiteralPath $vstest) 'The isolated Windows policy test needs the .NET test platform.'
    $trxName = 'installed-product-strict.trx'
    $trxPath = Join-Path $results $trxName
    if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath }
    # Session 0 isolates the real process blocker from this runner's PowerShell session.
    $arguments = 'exec --runtimeconfig "' + [IO.Path]::ChangeExtension($dll, 'runtimeconfig.json') + '" "' + $vstest + '" "' + $dll +
        '" /TestCaseFilter:FullyQualifiedName~StrictWindowsPolicyEndToEndTests "/ResultsDirectory:' + $results +
        '" "/Logger:trx;LogFileName=' + $trxName + '" /Environment:AXORITH_E2E_SYSTEM_POLICIES=1 "/Environment:AXORITH_E2E_INSTALL_ROOT=' + $installRoot +
        '" /Environment:AXORITH_DEV=1 /Environment:AXORITH_TELEMETRY_DISABLED=1 /Environment:AXORITH_TEST_MODE=1 -- RunConfiguration.TreatNoTestsAsError=true'
    $taskName = 'AxorithE2E-Policies-' + [guid]::NewGuid().ToString('N')
    $action = New-ScheduledTaskAction -Execute "$env:ProgramFiles\dotnet\dotnet.exe" -Argument $arguments
    $principal = New-ScheduledTaskPrincipal -UserId SYSTEM -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 2)
    try {
        Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings | Out-Null
        Start-ScheduledTask -TaskName $taskName
        $timer = [Diagnostics.Stopwatch]::StartNew()
        do {
            Start-Sleep -Milliseconds 250
            $info = Get-ScheduledTaskInfo -TaskName $taskName
            if ($timer.Elapsed.TotalMinutes -gt 2) { throw 'The isolated Windows policy test timed out.' }
        } while ($info.LastRunTime.Year -lt 2000 -or (Get-ScheduledTask -TaskName $taskName).State -eq 'Running')
        Assert-Condition ($info.LastTaskResult -eq 0) "Isolated Windows policy test failed: $($info.LastTaskResult)."
        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        Assert-Condition ([int]$counters.executed -eq 2 -and [int]$counters.passed -eq 2 -and [int]$counters.total -eq 2) 'Both real Windows policy and recovery-startup scenarios must execute and pass under guest SYSTEM.'
        Write-Host 'PASS: real Windows policies, owned process blocking, exact restoration and recovery-startup ownership.'
    }
    finally {
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($task) {
            if ($task.State -eq 'Running') { Stop-ScheduledTask -TaskName $taskName }
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        }
    }
}

try {
    if ($RequireCleanRuntime) {
        $runtimes = if (Test-Path "$env:ProgramFiles\dotnet\dotnet.exe") { & "$env:ProgramFiles\dotnet\dotnet.exe" --list-runtimes 2>$null } else { @() }
        Assert-Condition (-not ($runtimes -match '^Microsoft\.(WindowsDesktop|AspNetCore)\.App 10\.')) 'Clean-runtime E2E requires a fresh Windows image without desktop/ASP.NET 10 runtimes.'
    }
    Invoke-Setup $installer
    Assert-Installed
    Test-InstalledApis 'fresh'
    Test-ClientStartup $installRoot
    Write-Host 'PASS: fresh install, installed versions, real Host/Client API flow, Shim protocol, browser page blocking/unblocking, graceful shutdown.'

    # Use valid user data and verify byte-for-byte preservation through repair, upgrade and uninstall.
    $userRoot = Join-Path $env:APPDATA 'Axorith'
    $presetDirectory = Join-Path $userRoot 'presets'
    New-Item -ItemType Directory -Force $presetDirectory | Out-Null
    $presetPath = Join-Path $presetDirectory 'f50cbdb0-6b6c-457c-86a8-3069d1f95679.json'
    $presetText = '{"Id":"f50cbdb0-6b6c-457c-86a8-3069d1f95679","Name":"Preserve upgrade data","Version":4,"Modules":[]}'
    [IO.File]::WriteAllText($presetPath, $presetText)
    $clientAssembly = Join-Path $installRoot 'Axorith.Client/Axorith.Client.dll'
    $clientHash = (Get-FileHash -LiteralPath $clientAssembly).Hash
    $copyLock = [IO.File]::Open($clientAssembly, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { Invoke-Setup $installer '/S' 2 }
    finally { $copyLock.Dispose() }
    Assert-Condition ((Get-FileHash -LiteralPath $clientAssembly).Hash -eq $clientHash) 'Failed file replacement damaged the locked Client assembly.'
    Assert-Condition ([IO.File]::ReadAllText($presetPath) -eq $presetText) 'Failed file replacement changed user data.'
    Assert-Installed
    Write-Host 'PASS: locked product assembly prevents false install success and preserves the existing file and user data.'
    Invoke-Setup $installer
    Assert-Installed
    Assert-Condition ([IO.File]::ReadAllText($presetPath) -eq $presetText) 'Repair changed user data.'
    Write-Host 'PASS: same-version repair preserves user data.'

    if ($RequireCleanRuntime) {
        Set-GuestAspNetRuntimeUnavailable
        Invoke-Setup $installer -TimeoutSeconds 120
        Assert-Installed
        Assert-Condition ([IO.File]::ReadAllText($presetPath) -eq $presetText) 'Repair after runtime removal changed user data.'
        Test-InstalledApis 'runtime-repaired'
        Test-ClientStartup $installRoot
        Write-Host 'PASS: repair restores a missing ASP.NET runtime before its maintenance safety check and runs the installed product.'
    }
    else { Write-Host 'UNAVAILABLE: clean-image runtime-removal repair; use -RequireCleanRuntime in Windows Sandbox.' }

    $uninstallerCopy = Join-Path $results 'axorith-uninstall-e2e.exe'
    Copy-Item -LiteralPath (Join-Path $installRoot 'uninstall.exe') -Destination $uninstallerCopy -Force
    # _?= runs this copied uninstaller in place so its real exit code and cleanup can be awaited.
    $lockPath = Join-Path $installRoot 'e2e-locked-file'
    $lock = [IO.File]::Open($lockPath, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { Invoke-Setup $uninstallerCopy "/S _?=$installRoot" 2 -TimeoutSeconds 60 }
    finally { $lock.Dispose() }
    Assert-Condition ((Test-Path $uninstallKey) -or (Test-Path $legacyKey)) 'Failed uninstall removed retry registration.'
    $retryUninstaller = Join-Path $installRoot 'uninstall.exe'
    Assert-Condition (Test-Path -LiteralPath $retryUninstaller) 'Failed uninstall left a retry registration without a runnable uninstaller.'
    Assert-Condition ((Get-FileHash -LiteralPath $retryUninstaller).Hash -eq (Get-FileHash -LiteralPath $uninstallerCopy).Hash) 'Failed uninstall did not preserve the actual uninstaller for retry.'
    Copy-Item -LiteralPath $retryUninstaller -Destination $uninstallerCopy -Force
    Invoke-Setup $uninstallerCopy "/S _?=$installRoot" -TimeoutSeconds 60
    Assert-Condition (-not (Test-Path -LiteralPath $installRoot)) 'Uninstall left product files behind.'
    Assert-Condition (-not ((Test-Path $uninstallKey) -or (Test-Path $legacyKey))) 'Uninstall registration remains.'
    Assert-NativeRegistrationRemoved
    Assert-Condition ([IO.File]::ReadAllText($presetPath) -eq $presetText) 'Uninstall deleted user data.'
    Write-Host 'PASS: locked-file uninstall fails, retains retry registration, retry removes files and preserves user data.'

    if (-not $PreviousInstallerPath) {
        $PreviousInstallerPath = Join-Path $results 'previous-setup.exe'
        if (-not (Test-Path -LiteralPath $PreviousInstallerPath)) {
            $releases = Invoke-RestMethod 'https://api.github.com/repos/axorithlabs/axorith/releases?per_page=100' -Headers $githubHeaders
            $release = $releases |
                Where-Object { -not $_.draft -and -not $_.prerelease -and $_.tag_name -match '^v?(\d+\.\d+\.\d+)$' -and [version]$Matches[1] -lt [version]$expected } |
                Sort-Object { [version]$_.tag_name.TrimStart('v') } -Descending | Select-Object -First 1
            Assert-Condition ($null -ne $release) 'No older public release is available for upgrade E2E.'
            $asset = @($release.assets | Where-Object name -eq 'axorith-setup.exe')
            Assert-Condition ($asset.Count -eq 1) 'Previous public release installer is missing.'
            Invoke-WebRequest $asset[0].browser_download_url -OutFile $PreviousInstallerPath -UseBasicParsing
        }
    }
    $previousVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path -LiteralPath $PreviousInstallerPath).Path).ProductVersion
    Assert-Condition ([version]$previousVersion -lt [version]$expected) 'Upgrade E2E requires a strictly older installer; same-version repair is covered separately.'
    Invoke-Setup $PreviousInstallerPath
    $previousRoot = $installRoot
    if (-not (Test-Path (Join-Path $previousRoot 'Axorith.Client/Axorith.Client.exe'))) {
        $previousRoot = Join-Path $env:LOCALAPPDATA 'Programs/Axorith'
    }
    Test-ClientStartup $previousRoot
    $env:AXORITH_E2E_INSTALL_ROOT = $installRoot
    $env:AXORITH_E2E_PREVIOUS_INSTALL_ROOT = $previousRoot
    $env:AXORITH_E2E_UPDATE = '1'
    try { Invoke-InstalledTests 'FullyQualifiedName~InstalledUpdateEndToEndTests' 'installed-product-update.trx' 1 }
    finally {
        Remove-Item Env:AXORITH_E2E_UPDATE -ErrorAction SilentlyContinue
        Remove-Item Env:AXORITH_E2E_PREVIOUS_INSTALL_ROOT -ErrorAction SilentlyContinue
    }
    Assert-Installed
    Assert-Condition ([IO.File]::ReadAllText($presetPath) -eq $presetText) 'Upgrade changed user data.'
    Test-InstalledApis 'upgraded'
    Test-ClientStartup $installRoot
    Write-Host 'PASS: upgrade from public release with a running Client preserves data and runs the new Host.'
    Test-IsolatedWindowsPolicies

    Copy-Item -LiteralPath (Join-Path $installRoot 'uninstall.exe') -Destination $uninstallerCopy -Force
    if ($RequireCleanRuntime) { Set-GuestAspNetRuntimeUnavailable }
    Invoke-Setup $uninstallerCopy "/S _?=$installRoot" -TimeoutSeconds 120
    Assert-Condition (-not (Test-Path -LiteralPath $installRoot)) 'Final uninstall left product files.'
    Assert-Condition (-not ((Test-Path $uninstallKey) -or (Test-Path $legacyKey))) 'Final uninstall left product registration.'
    Assert-Condition ([IO.File]::ReadAllText($presetPath) -eq $presetText) 'Final uninstall deleted user data.'
    Assert-NativeRegistrationRemoved
    Write-Host 'PASS: installed-product E2E completed.'
}
finally {
    foreach ($process in $owned) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.Dispose()
    }
    Remove-Item Env:AXORITH_E2E_INSTALL_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:AXORITH_E2E_BROWSER_EXE -ErrorAction SilentlyContinue
    Remove-Item Env:AXORITH_E2E_EDGE_EXE -ErrorAction SilentlyContinue
    Remove-Item Env:AXORITH_E2E_FIREFOX_EXE -ErrorAction SilentlyContinue
    Remove-Item Env:AXORITH_E2E_GECKODRIVER_EXE -ErrorAction SilentlyContinue
}
