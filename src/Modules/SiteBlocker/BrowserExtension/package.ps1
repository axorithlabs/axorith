param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Chrome", "Firefox")]
    [string]$Browser
)

$root = $PSScriptRoot
$browserDir = Join-Path $root $Browser
$commonDir = Join-Path $root "Common"
$archiveName = if ($Browser -eq "Chrome") { "chrome-extension-latest.zip" } else { "site-blocker-extension-firefox.zip" }
$output = Join-Path $browserDir $archiveName
$staging = Join-Path ([IO.Path]::GetTempPath()) ("axorith-site-blocker-" + [guid]::NewGuid())

try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    Copy-Item (Join-Path $browserDir "manifest.json") $staging
    Copy-Item (Join-Path $commonDir "*") $staging
    if (Test-Path $output) { Remove-Item $output -Force }
    Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $output -Force
    Write-Host "Created $output"
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
