<#
.SYNOPSIS
  Automated PharmaBill deployment script for IT administrators and bulk installations.
.DESCRIPTION
  This script performs silent installation of PharmaBill and, when run as
  Administrator, pre-creates the LAN firewall rule. Antivirus settings are never
  changed. Useful for Group Policy, batch deployment, or remote endpoint management.
.PARAMETER InstallerPath
  Path to PharmaBill_Setup.exe. Defaults to: artifacts\installer\PharmaBill_Setup.exe
.PARAMETER Silent
  Perform silent installation (no GUI wizard). Default: $false (show wizard).
.PARAMETER ConfigureFirewall
  Automatically add Windows Firewall rule for port 5184 (Wi-Fi sync). Default: $true
.PARAMETER InstallDir
  Custom installation directory. Default: %LocalAppData%\Programs\PharmaBill
.PARAMETER LogPath
  Path to log deployment results. Default: %TEMP%\PharmaBill_Deploy.log
.EXAMPLE
  .\Deploy-PharmaBill.ps1 -Silent -ConfigureFirewall

  Performs silent installation and (if elevated) creates the LAN firewall rule.
.EXAMPLE
  .\Deploy-PharmaBill.ps1 -InstallerPath "\\server\share\PharmaBill_Setup.exe" -LogPath "C:\Logs\Deploy.log"

  Deploys from network share and logs to specified path.
#>
[CmdletBinding()]
param(
    [string]$InstallerPath = "artifacts\installer\PharmaBill_Setup.exe",
    [switch]$Silent,
    [switch]$ConfigureFirewall = $true,
    [string]$InstallDir,
    [string]$LogPath = "$env:TEMP\PharmaBill_Deploy.log"
)

$ErrorActionPreference = 'Stop'
$startTime = Get-Date

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $logMessage = "[$timestamp] [$Level] $Message"
    Write-Host $logMessage
    Add-Content -Path $LogPath -Value $logMessage -ErrorAction SilentlyContinue
}

function Test-Admin {
    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

Write-Log "==============================================="
Write-Log "PharmaBill Deployment Script"
Write-Log "==============================================="

# Check for admin privileges
if (-not (Test-Admin)) {
    Write-Log "⚠️  WARNING: Not running as Administrator. The firewall rule will be skipped; Windows will prompt on first launch instead." -Level 'WARN'
    Write-Log "Recommended: Run as Administrator for full deployment." -Level 'WARN'
}

# Verify installer exists
if (-not (Test-Path $InstallerPath)) {
    Write-Log "ERROR: Installer not found at: $InstallerPath" -Level 'ERROR'
    exit 1
}

$installerFullPath = (Resolve-Path $InstallerPath).Path
$installerSize = (Get-Item $installerFullPath).Length / 1MB
Write-Log "Installer found: $installerFullPath ($([Math]::Round($installerSize, 2)) MB)"

# Build installation arguments
$installArgs = @()

if ($Silent) {
    $installArgs += '/VERYSILENT'
    $installArgs += '/NORESTART'
    Write-Log "Silent installation mode enabled"
} else {
    Write-Log "Interactive installation mode (wizard will display)"
}

if ($InstallDir) {
    $installArgs += "/DIR=`"$InstallDir`""
    Write-Log "Custom install directory: $InstallDir"
}

# Execute installer
Write-Log "Launching installer..."
try {
    $process = Start-Process -FilePath $installerFullPath `
                            -ArgumentList $installArgs `
                            -Wait `
                            -PassThru

    if ($process.ExitCode -eq 0) {
        Write-Log "✓ Installation completed successfully" -Level 'SUCCESS'
    } else {
        Write-Log "⚠️  Installer exited with code: $($process.ExitCode)" -Level 'WARN'
    }
} catch {
    Write-Log "ERROR: Failed to launch installer: $_" -Level 'ERROR'
    exit 1
}

# Wait for file system to settle
Start-Sleep -Seconds 2

# Configure Windows Firewall (if requested and admin)
if ($ConfigureFirewall -and (Test-Admin)) {
    Write-Log "Configuring Windows Firewall for port 5184 (Wi-Fi sync)..."

    # Find PharmaBill.App.exe in Program Files or LocalAppData
    $possiblePaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\PharmaBill\PharmaBill.App.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "PharmaBill\PharmaBill.App.exe"),
        (Join-Path $env:ProgramFiles "PharmaBill\PharmaBill.App.exe")
    )

    $exePath = $possiblePaths | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($exePath) {
        try {
            # Remove old rule if exists
            netsh advfirewall firewall delete rule name="PharmaBill Wi-Fi Sync" 2>&1 | Out-Null

            # Add new rule
            netsh advfirewall firewall add rule `
                name="PharmaBill Wi-Fi Sync" `
                dir=in action=allow protocol=TCP localport=5184 `
                program="$exePath" `
                enable=yes description="Allow PharmaBill Wi-Fi sync with Android Mobile Bridge" | Out-Null

            Write-Log "✓ Firewall rule configured: TCP port 5184 opened for Wi-Fi sync" -Level 'SUCCESS'
        } catch {
            Write-Log "⚠️  Failed to configure firewall: $_" -Level 'WARN'
        }
    } else {
        Write-Log "⚠️  PharmaBill.App.exe not found in common locations; firewall rule skipped" -Level 'WARN'
    }
}

# Summary
$endTime = Get-Date
$duration = ($endTime - $startTime).TotalSeconds

Write-Log "==============================================="
Write-Log "Deployment completed in $([Math]::Round($duration, 2)) seconds" -Level 'SUCCESS'
Write-Log "==============================================="
Write-Log ""
Write-Log "Next Steps:"
Write-Log "  1. Verify PharmaBill launches: Desktop → PharmaBill or Start Menu"
Write-Log "  2. Configure pharmacy profile on first launch"
Write-Log "  3. Set up thermal printer (if applicable): Settings → Printer Configuration"
Write-Log "  4. Create initial backup: Settings → Backup & Restore"
Write-Log ""
Write-Log "Support:"
Write-Log "  Logs: $LogPath"
Write-Log "  Data: %LocalAppData%\PharmaBill"
Write-Log ""

if (Test-Path $LogPath) {
    Write-Log "Log file saved: $LogPath"
}
