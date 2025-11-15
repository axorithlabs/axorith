# Uninstall Axorith.Host Windows Service
# Requires Administrator privileges

param(
    [Parameter(Mandatory=$false)]
    [string]$ServiceName = "AxorithHostService"
)

# Check for administrator privileges
$currentUser = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentUser.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script requires Administrator privileges. Please run as Administrator."
    exit 1
}

Write-Host "Uninstalling Axorith Host Windows Service..."

# Check if service exists
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Warning "Service '$ServiceName' not found. Nothing to uninstall."
    exit 0
}

# Stop service if running
if ($service.Status -eq 'Running') {
    Write-Host "Stopping service..."
    try {
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
        Write-Host "Service stopped successfully."
    }
    catch {
        Write-Warning "Failed to stop service: $_"
    }
    
    Start-Sleep -Seconds 2
}

# Delete service
Write-Host "Removing service..."
$result = sc.exe delete $ServiceName

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Service uninstalled successfully!" -ForegroundColor Green
} else {
    Write-Error "Failed to remove service. Error code: $LASTEXITCODE"
    Write-Host "You may need to restart your computer and try again."
    exit 1
}

Write-Host ""
Write-Host "Service '$ServiceName' has been removed."
