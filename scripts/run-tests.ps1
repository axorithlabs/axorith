param(
    [string]$Filter = "",
    [switch]$Coverage,
    [switch]$OpenReport,
    [switch]$Verbose,
    [ValidateSet("Sdk", "Core", "Shared", "Contracts", "Host", "Integrations", "All")]
    [string]$Project = "All",
    [ValidateSet("All", "Unit", "Integration")]
    [string]$Suite = "All",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoBuild,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:AXORITH_TEST_MODE = '1'
$env:AXORITH_DEV = '1'
$env:AXORITH_TELEMETRY_DISABLED = '1'
$env:AXORITH_TELEMETRY_API_KEY = '##E2E_DISABLED##'
$env:AXORITH_TELEMETRY_HOST = 'http://127.0.0.1:9'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$results = Join-Path $root "build/$Configuration"
New-Item -ItemType Directory -Force $results | Out-Null
$projects = @('Sdk', 'Core', 'Shared', 'Contracts', 'Host', 'Integrations')
if ($Project -ne 'All') { $projects = @($Project) }
if ($Suite -eq 'Unit') { $projects = @($projects | Where-Object { $_ -ne 'Integrations' }) }
if ($Suite -eq 'Integration') { $projects = @($projects | Where-Object { $_ -eq 'Integrations' }) }
if ($projects.Count -eq 0) { throw 'The selected project and suite contain no test projects.' }

if ($Clean) {
    & dotnet clean (Join-Path $root 'Axorith.sln') -c $Configuration --verbosity quiet
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
foreach ($name in $projects) {
    # Coverlet adds roots to collectible assemblies. Verify lifetime normally, then measure the other paths.
    if ($Coverage -and $name -eq 'Integrations') {
        $lifetimeArgs = @{ Project = 'Integrations'; Configuration = $Configuration; NoBuild = $NoBuild }
        & $PSCommandPath @lifetimeArgs
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Copy-Item -LiteralPath (Join-Path $results 'Axorith.Integrations.Tests.trx') `
            -Destination (Join-Path $results 'Axorith.Integrations.Tests.uninstrumented.trx') -Force
    }
    $target = Join-Path $root "tests/Axorith.$name.Tests/Axorith.$name.Tests.csproj"
    $trxName = "Axorith.$name.Tests.trx"
    $trxPath = Join-Path $results $trxName
    if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath }
    $testArgs = @('test', $target, '-c', $Configuration, '--results-directory', $results,
        '--logger', "trx;LogFileName=$trxName", '--verbosity', $(if ($Verbose) { 'normal' } else { 'minimal' }))
    if ($NoBuild) { $testArgs += '--no-build' }
    $effectiveFilter = $Filter
    if ($Coverage -and $name -eq 'Integrations') {
        $excludeLifetime = 'FullyQualifiedName!~RealModulesWithPrivateDependenciesUnloadAfterHostShutdown'
        $effectiveFilter = if ($Filter) { "($Filter)&$excludeLifetime" } else { $excludeLifetime }
    }
    if ($effectiveFilter) { $testArgs += @('--filter', $effectiveFilter) }
    if ($Coverage) { $testArgs += '--collect:XPlat Code Coverage' }
    $testArgs += @('--', 'RunConfiguration.TreatNoTestsAsError=true')
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (-not (Test-Path -LiteralPath $trxPath)) { throw "Missing test results: $trxPath" }
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.executed -eq 0) { throw "$name executed zero tests (all skipped or unmatched filter)." }
    $skipped = @($trx.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'NotExecuted').Count
    Write-Host "$name : executed=$($counters.executed), passed=$($counters.passed), failed=$($counters.failed), skipped=$skipped"
}
if ($OpenReport) {
    if (-not $Coverage) { throw '-OpenReport requires -Coverage.' }
    $reportGenerator = Get-Command reportgenerator -ErrorAction SilentlyContinue
    if (-not $reportGenerator) { throw 'ReportGenerator is not installed. Raw coverage XML is available in the build directory.' }
    & $reportGenerator.Source "-reports:$results/**/coverage.cobertura.xml" "-targetdir:$results/coverage-report" '-reporttypes:Html'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Start-Process (Join-Path $results 'coverage-report/index.html')
}
