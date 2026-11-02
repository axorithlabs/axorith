#Requires -Version 7.0
param([switch]$RequireCleanRuntime)
$ErrorActionPreference = 'Stop'
$sandbox = Join-Path $env:WINDIR 'System32/WindowsSandbox.exe'
if (-not (Test-Path -LiteralPath $sandbox)) { throw 'Windows Sandbox is unavailable. Run test-installer.ps1 on the disposable CI runner; no system features were changed.' }
if (Get-Process WindowsSandbox,WindowsSandboxClient -ErrorAction SilentlyContinue) {
    throw 'A Windows Sandbox session is already running; this runner must create and own its disposable session.'
}
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$results = Join-Path $root 'build/Release'
$installer = Join-Path $root 'build/Installer'
if (-not (Test-Path (Join-Path $installer 'axorith-setup.exe'))) { throw 'Build the release installer first.' }
$sdk = (& dotnet --list-sdks | Select-Object -Last 1) -split ' ', 2
$sdkDirectory = Join-Path $sdk[1].Trim('[', ']') $sdk[0]
if (-not (Test-Path (Join-Path $sdkDirectory 'vstest.console.dll'))) { throw 'The .NET SDK test platform is missing.' }
$marker = Join-Path $results 'installer-e2e.exit'
if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
foreach ($name in @('installer-e2e.log', 'browser-e2e.log', 'firefox-e2e.log', 'host-process-e2e.log',
    'installed-product-fresh.trx', 'installed-product-upgraded.trx', 'installed-product-runtime-repaired.trx', 'installed-product-strict.trx', 'installed-product-update.trx')) {
    $previousResult = Join-Path $results $name
    if (Test-Path -LiteralPath $previousResult) { Remove-Item -LiteralPath $previousResult }
}
$clean = if ($RequireCleanRuntime) { ' -RequireCleanRuntime' } else { '' }
$command = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try { $env:AXORITH_DEV = ''1''; $env:AXORITH_TEST_MODE = ''1''; $env:AXORITH_TELEMETRY_DISABLED = ''1''; $env:AXORITH_TELEMETRY_API_KEY = ''##E2E_DISABLED##''; $env:AXORITH_TELEMETRY_HOST = ''http://127.0.0.1:9''; $env:AXORITH_VSTEST_PATH = ''C:\VSTest\vstest.console.dll''; & C:\Axorith\scripts\test-installer.ps1' + $clean + ' *> C:\Axorith\build\Release\installer-e2e.log; Set-Content C:\Axorith\build\Release\installer-e2e.exit 0 } catch { $_ | Out-File C:\Axorith\build\Release\installer-e2e.log -Append; Set-Content C:\Axorith\build\Release\installer-e2e.exit 1 }"'
function Escape-Xml([string]$Value) { [Security.SecurityElement]::Escape($Value) }
$configuration = @"
<Configuration>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <MappedFolders>
    <MappedFolder><HostFolder>$(Escape-Xml $PSScriptRoot)</HostFolder><SandboxFolder>C:\Axorith\scripts</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(Escape-Xml $results)</HostFolder><SandboxFolder>C:\Axorith\build\Release</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(Escape-Xml $installer)</HostFolder><SandboxFolder>C:\Axorith\build\Installer</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(Escape-Xml $sdkDirectory)</HostFolder><SandboxFolder>C:\VSTest</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>$(Escape-Xml $command)</Command></LogonCommand>
</Configuration>
"@
$path = Join-Path $results 'installer-e2e.wsb'
[IO.File]::WriteAllText($path, $configuration)
$process = Start-Process -FilePath $sandbox -ArgumentList ('"' + $path + '"') -PassThru -WindowStyle Hidden
try {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $marker)) {
        if ($timer.Elapsed.TotalMinutes -gt 20) { throw 'Installer E2E timed out; inspect build/Release/installer-e2e.log.' }
        Start-Sleep -Seconds 1
    }
    if ([int](Get-Content -LiteralPath $marker) -ne 0) { throw 'Installer E2E failed; inspect build/Release/installer-e2e.log.' }
    Write-Host 'Windows Sandbox installer E2E passed.'
}
finally {
    if (-not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(10000) }
    $process.Dispose()
}
