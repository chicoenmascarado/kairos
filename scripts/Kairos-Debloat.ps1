<#
.SYNOPSIS
    Kairos Debloat - Phase 1 base optimization script.

.DESCRIPTION
    Removes common Windows 11 bloatware apps, disables unnecessary services,
    and applies first-pass privacy/performance tweaks.

    Design principles:
      - SAFE: every change is conservative and well-understood.
      - REVERSIBLE: creates a System Restore point and logs everything.
      - TRANSPARENT: prints exactly what it does. No silent changes.
      - MODULAR: each section can be commented out independently.

    This is the foundation of the Kairos playbook. It is meant to be run on a
    user's own Windows installation (Enforcement model A - see project docs).

.NOTES
    Project : Kairos
    Phase   : 1 - Optimized Base
    Run as  : Administrator
    Tested  : Windows 11 (VM lab). DO NOT run on a production machine yet.
#>

#Requires -RunAsAdministrator

# ============================================================================
#  0. SETUP & SAFETY
# ============================================================================

$ErrorActionPreference = 'Continue'   # keep going if one item fails
$KairosVersion = '0.1.0-dev'

# --- Logging ---------------------------------------------------------------
$LogDir  = "$env:ProgramData\Kairos\logs"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$LogFile = Join-Path $LogDir ("debloat_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))

function Write-Kairos {
    param(
        [string]$Message,
        [ValidateSet('INFO','OK','WARN','ERROR','STEP')] [string]$Level = 'INFO'
    )
    $colors = @{ INFO='Gray'; OK='Green'; WARN='Yellow'; ERROR='Red'; STEP='Cyan' }
    $tag    = @{ INFO='  '; OK='[OK]'; WARN='[!]'; ERROR='[X]'; STEP='==>' }
    $line   = "{0} {1}" -f $tag[$Level], $Message
    Write-Host $line -ForegroundColor $colors[$Level]
    "{0:HH:mm:ss} [{1}] {2}" -f (Get-Date), $Level, $Message | Out-File -FilePath $LogFile -Append -Encoding utf8
}

# --- Banner ----------------------------------------------------------------
Clear-Host
Write-Host ""
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host "            K A I R O S" -ForegroundColor Magenta
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host ""
Write-Host "  Kairos Debloat  -  v$KairosVersion  -  Phase 1" -ForegroundColor White
Write-Host "  The perfect moment when everything flows." -ForegroundColor DarkGray
Write-Host "  -------------------------------------" -ForegroundColor DarkGray
Write-Host ""

Write-Kairos "Log file: $LogFile" 'INFO'

# --- Confirmation gate -----------------------------------------------------
Write-Host ""
Write-Kairos "This script will modify Windows: remove some apps, disable some" 'WARN'
Write-Kairos "services, and apply privacy tweaks. Run ONLY in a test VM for now." 'WARN'
Write-Host ""
$confirm = Read-Host "  Type 'KAIROS' to continue, anything else to abort"
if ($confirm -ne 'KAIROS') {
    Write-Kairos "Aborted by user. Nothing was changed." 'INFO'
    return
}

# --- Restore point (safety net) --------------------------------------------
Write-Host ""
Write-Kairos "Creating a System Restore point (safety net)..." 'STEP'
try {
    Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
    Checkpoint-Computer -Description "Kairos Debloat $KairosVersion" -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
    Write-Kairos "Restore point created." 'OK'
} catch {
    Write-Kairos "Could not create a restore point ($($_.Exception.Message))." 'WARN'
    Write-Kairos "Continuing anyway - but make sure you have a VM snapshot." 'WARN'
}

# ============================================================================
#  1. REMOVE BLOATWARE APPS  (UWP / Store apps)
# ============================================================================
#  These are safe-to-remove consumer apps. The user can reinstall any of them
#  from the Store later. We deliberately KEEP: Store, Terminal, Calculator,
#  Photos, Snipping Tool, and anything security-related.

Write-Host ""
Write-Kairos "Removing bloatware apps..." 'STEP'

$BloatApps = @(
    'Microsoft.3DBuilder'
    'Microsoft.BingNews'
    'Microsoft.BingWeather'
    'Microsoft.BingFinance'
    'Microsoft.BingSports'
    'Microsoft.GamingApp'                 # Xbox app (remove if not gaming-focused build)
    'Microsoft.GetHelp'
    'Microsoft.Getstarted'                # "Tips"
    'Microsoft.Microsoft3DViewer'
    'Microsoft.MicrosoftOfficeHub'        # "Get Office" nag
    'Microsoft.MicrosoftSolitaireCollection'
    'Microsoft.MixedReality.Portal'
    'Microsoft.People'
    'Microsoft.PowerAutomateDesktop'
    'Microsoft.SkypeApp'
    'Microsoft.Todos'
    'Microsoft.WindowsAlarms'
    'Microsoft.WindowsFeedbackHub'
    'Microsoft.WindowsMaps'
    'Microsoft.YourPhone'                 # "Phone Link" (keep if you use it)
    'Microsoft.ZuneMusic'                 # Media Player (old Groove)
    'Microsoft.ZuneVideo'
    'MicrosoftTeams'                      # consumer Teams
    'Clipchamp.Clipchamp'
)

$removed = 0; $skipped = 0
foreach ($app in $BloatApps) {
    $pkg = Get-AppxPackage -Name $app -ErrorAction SilentlyContinue
    if ($pkg) {
        try {
            $pkg | Remove-AppxPackage -ErrorAction Stop
            Write-Kairos "Removed: $app" 'OK'
            $removed++
            # also remove the provisioned copy so it doesn't return for new users
            Get-AppxProvisionedPackage -Online |
                Where-Object DisplayName -EQ $app |
                Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Out-Null
        } catch {
            Write-Kairos "Could not remove $app ($($_.Exception.Message))" 'WARN'
        }
    } else {
        $skipped++
    }
}
Write-Kairos "Apps removed: $removed   -   not present / skipped: $skipped" 'INFO'

# ============================================================================
#  2. DISABLE UNNECESSARY SERVICES
# ============================================================================
#  Conservative list. We set startup to Manual/Disabled, never delete.
#  Each can be re-enabled with: Set-Service <name> -StartupType Automatic

Write-Host ""
Write-Kairos "Adjusting unnecessary services..." 'STEP'

# Service name => desired startup type
$Services = @{
    'DiagTrack'              = 'Disabled'   # Connected User Experiences / Telemetry
    'dmwappushservice'       = 'Disabled'   # WAP Push (telemetry related)
    'RetailDemo'             = 'Disabled'   # Retail demo mode
    'MapsBroker'             = 'Manual'     # Downloaded Maps Manager
    'WMPNetworkSvc'          = 'Manual'     # WMP network sharing
    'Fax'                    = 'Disabled'   # Fax
    'XblAuthManager'         = 'Manual'     # Xbox Live Auth (Manual, not Disabled)
    'XblGameSave'            = 'Manual'     # Xbox Live Game Save
    'XboxNetApiSvc'          = 'Manual'     # Xbox Live Networking
}

foreach ($svc in $Services.Keys) {
    $service = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($service) {
        try {
            Set-Service -Name $svc -StartupType $Services[$svc] -ErrorAction Stop
            if ($service.Status -eq 'Running' -and $Services[$svc] -eq 'Disabled') {
                Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
            }
            Write-Kairos "Service '$svc' -> $($Services[$svc])" 'OK'
        } catch {
            Write-Kairos "Could not change service '$svc' ($($_.Exception.Message))" 'WARN'
        }
    } else {
        Write-Kairos "Service '$svc' not found (already gone or N/A)" 'INFO'
    }
}

# ============================================================================
#  3. PRIVACY & UX TWEAKS  (registry)
# ============================================================================
#  All writes are logged. These are reversible by setting the value back.

Write-Host ""
Write-Kairos "Applying privacy and UX tweaks..." 'STEP'

function Set-KairosReg {
    param([string]$Path, [string]$Name, $Value, [string]$Type = 'DWord', [string]$Desc)
    try {
        if (-not (Test-Path $Path)) { New-Item -Path $Path -Force | Out-Null }
        Set-ItemProperty -Path $Path -Name $Name -Value $Value -Type $Type -ErrorAction Stop
        Write-Kairos "$Desc" 'OK'
    } catch {
        Write-Kairos "Tweak failed: $Desc ($($_.Exception.Message))" 'WARN'
    }
}

# Disable advertising ID
Set-KairosReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' 'Enabled' 0 'DWord' `
    'Disabled advertising ID'

# Disable "Suggested content" / tips in Settings
Set-KairosReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338393Enabled' 0 'DWord' `
    'Disabled suggested content in Settings'
Set-KairosReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SystemPaneSuggestionsEnabled' 0 'DWord' `
    'Disabled Start menu suggestions'

# Disable Start menu / search web results & Bing
Set-KairosReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' 'BingSearchEnabled' 0 'DWord' `
    'Disabled Bing in Windows Search'
Set-KairosReg 'HKCU:\Software\Policies\Microsoft\Windows\Explorer' 'DisableSearchBoxSuggestions' 1 'DWord' `
    'Disabled web suggestions in search box'

# Show file extensions (sane default for power users)
Set-KairosReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'HideFileExt' 0 'DWord' `
    'Enabled show file extensions'

# Disable lock-screen tips/ads
Set-KairosReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'RotatingLockScreenOverlayEnabled' 0 'DWord' `
    'Disabled lock-screen ads/tips'

# Disable telemetry (policy level) - requires admin, applies machine-wide
Set-KairosReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'AllowTelemetry' 0 'DWord' `
    'Set telemetry policy to minimum'

# ============================================================================
#  4. DONE
# ============================================================================
Write-Host ""
Write-Host "  -------------------------------------" -ForegroundColor DarkGray
Write-Kairos "Kairos debloat pass complete." 'OK'
Write-Kairos "A restart is recommended to apply all changes." 'INFO'
Write-Kairos "Full log saved to: $LogFile" 'INFO'
Write-Host ""
Write-Host "  To undo: restore your VM snapshot, or use System Restore." -ForegroundColor DarkGray
Write-Host ""
