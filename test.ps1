param(
    [string]$Output = "project_dump.txt"
)

# Explicit include entries
$includeEntries = @(
    "installer",
    "scripts",
    "src",
    "docs",
    "README.md",
    "CODE_OF_CONDUCT.md",
    "CONTRIBUTING.md",
    "LICENSE.md",
    "Axorith.sln",
    "Directory.Build.props",
    "Directory.Build.targets"
)

# Allowed extensions for source-like dirs
$allowedSrcExt = @(".cs", ".csproj", ".json", ".manifest", ".js", ".html", ".bat", ".ps1", ".axaml")

# Explicitly excluded extensions
$excludedExt = @(".zip", ".png")

# Collect all matching files
$files = @()
foreach ($entry in $includeEntries) {
    if (Test-Path $entry) {
        $item = Get-Item -LiteralPath $entry -ErrorAction SilentlyContinue
        if ($item.PSIsContainer) {
            switch -Regex ($entry) {
                "^src$" {
                    $files += Get-ChildItem -LiteralPath $entry -Recurse -File |
                        Where-Object { $_.Extension -in $allowedSrcExt }
                }
                "^extensions$" {
                    $files += Get-ChildItem -LiteralPath $entry -Recurse -File |
                        Where-Object { $_.Extension -in $allowedSrcExt }
                }
                "^docs$" {
                    $files += Get-ChildItem -LiteralPath $entry -Recurse -File |
                        Where-Object { $excludedExt -notcontains $_.Extension }
                }
                default {
                    $files += Get-ChildItem -LiteralPath $entry -Recurse -File |
                        Where-Object { $excludedExt -notcontains $_.Extension }
                }
            }
        } else {
            if ($excludedExt -notcontains $item.Extension) {
                $files += $item
            }
        }
    } else {
        Write-Host "⚠️ Not found (skipped): $entry"
    }
}

if (-not $files) {
    Write-Host "❌ No matching files found. Nothing to dump."
    exit 1
}

# Output all collected files into one
$files | Sort-Object FullName | ForEach-Object {
    # Make relative path for readability
    $relative = Resolve-Path -LiteralPath $_.FullName | ForEach-Object {
        $_ -replace [regex]::Escape((Resolve-Path ".").Path + "\"), ""
    }
    "===== FILE: $relative =====`r`n" +
    (Get-Content -LiteralPath $_.FullName -Raw -ErrorAction Stop) + "`r`n`r`n"
} | Out-File -LiteralPath $Output -Encoding UTF8

Write-Host "✅ Dump complete. Output file: $Output"
Write-Host "Files included: $($files.Count)"
