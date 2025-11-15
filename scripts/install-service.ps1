# Install Axorith.Host as Windows Service
# Requires Administrator privileges

param(
    [Parameter(Mandatory=$false)]
    [string]$HostExePath = "",
    
    [Parameter(Mandatory=$false)]
    [string]$ServiceName = "AxorithHostService",
    
    [Parameter(Mandatory=$false)]
    [string]$DisplayName = "Axorith Host Service",
    
    [Parameter(Mandatory=$false)]
    [string]$Description = "Axorith Host Service"
)

# Check for administrator privileges
$currentUser = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentUser.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script requires Administrator privileges. Please run as Administrator."
    exit 1
}

# Find Axorith.Host.exe if not specified
if ([string]::IsNullOrEmpty($HostExePath)) {
    # Try to find in build output
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $possiblePaths = @(
        "$scriptDir\..\build\Release\Axorith.Host\Axorith.Host.exe",
        "$scriptDir\..\build\Debug\Axorith.Host\Axorith.Host.exe",
    )
    
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            $HostExePath = Resolve-Path $path
            Write-Host "Found Axorith.Host.exe at: $HostExePath"
            break
        }
    }
    
    if ([string]::IsNullOrEmpty($HostExePath)) {
        Write-Error "Could not find Axorith.Host.exe. Please specify -HostExePath parameter."
        exit 1
    }
}

# Verify executable exists
if (-not (Test-Path $HostExePath)) {
    Write-Error "Host executable not found at: $HostExePath"
    exit 1
}

Write-Host "Installing Axorith Host as Windows Service..."
Write-Host "  Service Name: $ServiceName"
Write-Host "  Executable: $HostExePath"

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Warning "Service '$ServiceName' already exists. Removing old service..."
    
    # Stop service if running
    if ($existingService.Status -eq 'Running') {
        Write-Host "Stopping service..."
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    
    # Delete service
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
}

# Create service using sc.exe
Write-Host "Creating service..."
$result = sc.exe create $ServiceName binPath= "`"$HostExePath`" --service" start= auto DisplayName= $DisplayName

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create service. Error code: $LASTEXITCODE"
    exit 1
}

# Set description
sc.exe description $ServiceName $Description

# Configure service recovery options
Write-Host "Configuring service recovery options..."
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000

# Start service
Write-Host "Starting service..."
Start-Service -Name $ServiceName

# Wait for service to start
Start-Sleep -Seconds 2

# Check service status
$service = Get-Service -Name $ServiceName
if ($service.Status -eq 'Running') {
    Write-Host "✓ Service installed and started successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Service Details:"
    Write-Host "  Name: $($service.Name)"
    Write-Host "  Display Name: $($service.DisplayName)"
    Write-Host "  Status: $($service.Status)"
    Write-Host "  Start Type: Automatic"
    Write-Host ""
    Write-Host "To manage the service, use:"
    Write-Host "  Start: Start-Service -Name $ServiceName"
    Write-Host "  Stop: Stop-Service -Name $ServiceName"
    Write-Host "  Status: Get-Service -Name $ServiceName"
    Write-Host "  Uninstall: Run uninstall-service.ps1"
} else {
    Write-Error "Service installed but failed to start. Status: $($service.Status)"
    exit 1
}
