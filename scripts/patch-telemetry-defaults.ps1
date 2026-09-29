param(
    [string]$Key = $env:POSTHOG_API_KEY,
    [string]$ApiHost = $env:POSTHOG_API_HOST
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Key)) {
    Write-Error "POSTHOG_API_KEY is not set. Provide -Key or set env:POSTHOG_API_KEY."
    exit 1
}
if ($Key -notmatch '^[A-Za-z0-9_-]{8,128}$') {
    Write-Error "POSTHOG_API_KEY must contain only letters, digits, underscores, or hyphens."
    exit 1
}
if ([string]::IsNullOrWhiteSpace($ApiHost)) {
    $ApiHost = "https://us.i.posthog.com"
}
if ($ApiHost -notmatch '^https://[A-Za-z0-9.-]+(?::[0-9]+)?(?:/[A-Za-z0-9._~/-]*)?/?$') {
    Write-Error "POSTHOG_API_HOST must be an HTTPS host URL."
    exit 1
}
$ApiHost = $ApiHost.TrimEnd('/')

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$telemetrySettingsPath = Join-Path $repoRoot "src/Telemetry/TelemetrySettings.cs"
$websiteScriptPath = Join-Path $repoRoot "website/js/posthog.js"

foreach ($path in @($telemetrySettingsPath, $websiteScriptPath)) {
    if (-not (Test-Path $path)) {
        Write-Error "Telemetry file not found at $path"
        exit 1
    }
}

$settings = Get-Content -Path $telemetrySettingsPath -Raw
$apiKeyPattern = 'public string PostHogApiKey \{ get; init; \} = "[^"]+";'
$apiHostPattern = 'public string PostHogHost \{ get; init; \} = "[^"]+";'
if ([regex]::Matches($settings, $apiKeyPattern).Count -ne 1 -or
    [regex]::Matches($settings, $apiHostPattern).Count -ne 1) {
    Write-Error "TelemetrySettings.cs no longer has the expected single PostHog defaults."
    exit 1
}
$settings = [regex]::Replace($settings,
    $apiKeyPattern,
    'public string PostHogApiKey { get; init; } = "' + $Key + '";', 1)
$settings = [regex]::Replace($settings,
    $apiHostPattern,
    'public string PostHogHost { get; init; } = "' + $ApiHost + '";', 1)
Set-Content -Path $telemetrySettingsPath -Value $settings -Encoding UTF8

$websiteScript = Get-Content -Path $websiteScriptPath -Raw
$websiteKeyPattern = 'const posthogProjectToken = "[^"]+";'
$websiteHostPattern = 'const posthogApiHost = "[^"]+";'
if ([regex]::Matches($websiteScript, $websiteKeyPattern).Count -ne 1 -or
    [regex]::Matches($websiteScript, $websiteHostPattern).Count -ne 1) {
    Write-Error "website/js/posthog.js no longer has the expected single PostHog defaults."
    exit 1
}
$websiteScript = [regex]::Replace($websiteScript,
    $websiteKeyPattern,
    'const posthogProjectToken = "' + $Key + '";', 1)
$websiteScript = [regex]::Replace($websiteScript,
    $websiteHostPattern,
    'const posthogApiHost = "' + $ApiHost + '";', 1)
Set-Content -Path $websiteScriptPath -Value $websiteScript -Encoding UTF8

Write-Host "Patched desktop and website PostHog defaults." -ForegroundColor Green
