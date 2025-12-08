param(
    [string]$Key = $env:POSTHOG_API_KEY
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Key)) {
    Write-Error "POSTHOG_API_KEY is not set. Provide -Key or set env:POSTHOG_API_KEY."
    exit 1
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$telemetrySettingsPath = Join-Path $repoRoot "src/Telemetry/TelemetrySettings.cs"

if (-not (Test-Path $telemetrySettingsPath)) {
    Write-Error "TelemetrySettings.cs not found at $telemetrySettingsPath"
    exit 1
}

$content = Get-Content -Path $telemetrySettingsPath -Raw

$apiKeyPattern = 'public string PostHogApiKey { get; init; } = "[^"]+";'
$apiKeyReplacement = 'public string PostHogApiKey { get; init; } = "' + $Key + '";'
$apiKeyRegex = [regex]::new($apiKeyPattern)

if (-not $apiKeyRegex.IsMatch($content)) {
    Write-Error "PostHogApiKey property not found for patching."
    exit 1
}

$content = $apiKeyRegex.Replace($content, $apiKeyReplacement, 1)

Set-Content -Path $telemetrySettingsPath -Value $content -Encoding UTF8
Write-Host "Patched TelemetrySettings.cs with PostHog defaults (API key and optional host)." -ForegroundColor Green

