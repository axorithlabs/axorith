# Axorith Test Runner
# PowerShell script for running tests locally with various options

param(
    [Parameter(HelpMessage="Test filter (e.g., 'FullyQualifiedName~ActionTests')")]
    [string]$Filter = "",
    
    [Parameter(HelpMessage="Generate coverage report")]
    [switch]$Coverage,
    
    [Parameter(HelpMessage="Open coverage report in browser")]
    [switch]$OpenReport,
    
    [Parameter(HelpMessage="Verbose output")]
    [switch]$Verbose,
    
    [Parameter(HelpMessage="Specific test project (Sdk, Core, Shared, or All)")]
    [ValidateSet("Sdk", "Core", "Shared", "All")]
    [string]$Project = "All",
    
    [Parameter(HelpMessage="Clean before running")]
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

Write-Host "🧪 Axorith Test Runner" -ForegroundColor Cyan
Write-Host "=====================`n" -ForegroundColor Cyan

# Determine solution/project path
$solutionPath = Join-Path $PSScriptRoot ".." "Axorith.sln"
$testProjects = @{
    "Sdk" = Join-Path $PSScriptRoot ".." "tests" "Axorith.Sdk.Tests" "Axorith.Sdk.Tests.csproj"
    "Core" = Join-Path $PSScriptRoot ".." "tests" "Axorith.Core.Tests" "Axorith.Core.Tests.csproj"
    "Shared" = Join-Path $PSScriptRoot ".." "tests" "Axorith.Shared.Tests" "Axorith.Shared.Tests.csproj"
}

# Clean if requested
if ($Clean) {
    Write-Host "🧹 Cleaning solution..." -ForegroundColor Yellow
    dotnet clean $solutionPath --verbosity quiet
    Write-Host "✅ Cleaned`n" -ForegroundColor Green
}

# Build arguments
$testArgs = @()

# Determine what to test
if ($Project -eq "All") {
    $testTarget = $solutionPath
    Write-Host "📦 Testing: All projects" -ForegroundColor Cyan
} else {
    $testTarget = $testProjects[$Project]
    Write-Host "📦 Testing: Axorith.$Project.Tests" -ForegroundColor Cyan
}

# Verbosity
if ($Verbose) {
    $testArgs += "--verbosity", "detailed"
} else {
    $testArgs += "--verbosity", "normal"
}

# Filter
if ($Filter) {
    $testArgs += "--filter", $Filter
    Write-Host "🔍 Filter: $Filter" -ForegroundColor Cyan
}

# Coverage
$coverageDir = Join-Path $PSScriptRoot ".." "coverage"
$reportDir = Join-Path $PSScriptRoot ".." "coverage-report"

if ($Coverage) {
    Write-Host "📊 Coverage: Enabled" -ForegroundColor Cyan
    $testArgs += "--collect:XPlat Code Coverage"
    $testArgs += "--results-directory", $coverageDir
    
    # Clean old coverage
    if (Test-Path $coverageDir) {
        Remove-Item $coverageDir -Recurse -Force
    }
}

Write-Host "`n⚡ Running tests...`n" -ForegroundColor Yellow

# Run tests
$testCommand = "dotnet test `"$testTarget`" $($testArgs -join ' ')"
Write-Host "Command: $testCommand`n" -ForegroundColor DarkGray

try {
    Invoke-Expression $testCommand
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`n❌ Tests failed!" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    
    Write-Host "`n✅ All tests passed!" -ForegroundColor Green
    
    # Generate coverage report
    if ($Coverage) {
        Write-Host "`n📈 Generating coverage report..." -ForegroundColor Yellow
        
        # Check if reportgenerator is installed
        $reportGenInstalled = dotnet tool list -g | Select-String "dotnet-reportgenerator-globaltool"
        
        if (-not $reportGenInstalled) {
            Write-Host "Installing ReportGenerator..." -ForegroundColor Yellow
            dotnet tool install -g dotnet-reportgenerator-globaltool
        }
        
        # Find coverage files
        $coverageFiles = Get-ChildItem -Path $coverageDir -Filter "coverage.cobertura.xml" -Recurse
        
        if ($coverageFiles.Count -eq 0) {
            Write-Host "⚠️ No coverage files found!" -ForegroundColor Yellow
        } else {
            $reports = ($coverageFiles | ForEach-Object { $_.FullName }) -join ";"
            
            # Generate report
            reportgenerator `
                -reports:$reports `
                -targetdir:$reportDir `
                -reporttypes:"Html;Badges;Cobertura" `
                -verbosity:Warning
            
            Write-Host "✅ Coverage report generated: $reportDir\index.html" -ForegroundColor Green
            
            # Open report
            if ($OpenReport) {
                $indexPath = Join-Path $reportDir "index.html"
                Write-Host "🌐 Opening report in browser..." -ForegroundColor Cyan
                Start-Process $indexPath
            }
        }
    }
    
} catch {
    Write-Host "`n❌ Error: $_" -ForegroundColor Red
    exit 1
}

Write-Host "`n🎉 Done!" -ForegroundColor Green
