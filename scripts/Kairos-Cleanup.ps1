<#
.SYNOPSIS
    Kairos Deep Cleanup - Phase 1, pass 2.

.DESCRIPTION
    Builds on Kairos-Debloat. Goes deeper:
      - Removes OneDrive completely
      - Disables Widgets, news feed, and CrossDeviceResume (frees ~700MB RAM)
      - Tunes the 'ASK' services per Kairos decisions
      - Disables telemetry/diagnostic scheduled tasks
      - Reduces Defender (see notes on Tamper Protection)

    SAFE / REVERSIBLE / TRANSPARENT / MODULAR (same principles as pass 1).
    Run ONLY in the test VM. Take a snapshot first.

.NOTES
    Project : Kairos
    Phase   : 1 - Optimized Base (deep cleanup)
    Run as  : Administrator
#>

#Requires -RunAsAdministrator

$ErrorActionPreference = 'Continue'
$KairosVersion = '0.1.0-dev'

# --- Logging ---------------------------------------------------------------
$LogDir  = "$env:ProgramData\Kairos\logs"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$LogFile = Join-Path $LogDir ("cleanup_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))

function Write-Kairos {
    param([string]$Message, [ValidateSet('INFO','OK','WARN','ERROR','STEP')] [string]$Level = 'INFO')
    $colors = @{ INFO='Gray'; OK='Green'; WARN='Yellow'; ERROR='Red'; STEP='Cyan' }
    $tag    = @{ INFO='  '; OK='[OK]'; WARN='[!]'; ERROR='[X]'; STEP='==>' }
    Write-Host ("{0} {1}" -f $tag[$Level], $Message) -ForegroundColor $colors[$Level]
    "{0:HH:mm:ss} [{1}] {2}" -f (Get-Date), $Level, $Message | Out-File -FilePath $LogFile -Append -Encoding utf8
}

function Set-KairosReg {
    param([string]$Path, [string]$Name, $Value, [string]$Type = 'DWord', [string]$Desc)
    try {
        if (-not (Test-Path $Path)) { New-Item -Path $Path -Force | Out-Null }
        Set-ItemProperty -Path $Path -Name $Name -Value $Value -Type $Type -ErrorAction Stop
        Write-Kairos "$Desc" 'OK'
    } catch { Write-Kairos "Tweak failed: $Desc ($($_.Exception.Message))" 'WARN' }
}

# --- banner ----------------------------------------------------------------
Clear-Host
Write-Host ""
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host "     K A I R O S   -   DEEP CLEANUP" -ForegroundColor Magenta
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host "  Phase 1, pass 2  -  v$KairosVersion" -ForegroundColor White
Write-Host ""

Write-Kairos "Log: $LogFile" 'INFO'
Write-Host ""
Write-Kairos "This makes deeper changes (removes OneDrive, disables widgets," 'WARN'
Write-Kairos "feed, some services & tasks, reduces Defender). VM ONLY." 'WARN'
Write-Host ""
$confirm = Read-Host "  Type 'KAIROS' to continue, anything else to abort"
if ($confirm -ne 'KAIROS') { Write-Kairos "Aborted. Nothing changed." 'INFO'; return }

# --- restore point ---------------------------------------------------------
Write-Host ""
Write-Kairos "Creating System Restore point..." 'STEP'
try {
    Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
    Checkpoint-Computer -Description "Kairos Cleanup $KairosVersion" -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
    Write-Kairos "Restore point created." 'OK'
} catch { Write-Kairos "No restore point ($($_.Exception.Message)). Rely on VM snapshot." 'WARN' }

# ============================================================================
#  1. REMOVE ONEDRIVE COMPLETELY
# ============================================================================
Write-Host ""
Write-Kairos "Removing OneDrive..." 'STEP'
try {
    # stop it if running
    Get-Process -Name OneDrive -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    # run the built-in uninstaller (32/64 bit paths)
    $odSetup = @("$env:SystemRoot\System32\OneDriveSetup.exe", "$env:SystemRoot\SysWOW64\OneDriveSetup.exe")
    foreach ($p in $odSetup) {
        if (Test-Path $p) { Start-Process $p '/uninstall' -Wait -ErrorAction SilentlyContinue }
    }

    # remove leftover startup entries
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'OneDrive' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'OneDriveSetup' -ErrorAction SilentlyContinue

    # prevent it from reinstalling for new setups
    Set-KairosReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive' 'DisableFileSyncNGSC' 1 'DWord' 'Blocked OneDrive sync (policy)'

    # remove the empty explorer sidebar entry
    Set-KairosReg 'HKCR:\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}' 'System.IsPinnedToNameSpaceTree' 0 'DWord' 'Removed OneDrive from Explorer sidebar' 2>$null

    Write-Kairos "OneDrive removed." 'OK'
} catch { Write-Kairos "OneDrive removal partial ($($_.Exception.Message))" 'WARN' }

# ============================================================================
#  2. DISABLE WIDGETS, NEWS FEED, CROSS-DEVICE  (the big RAM wins)
# ============================================================================
Write-Host ""
Write-Kairos "Disabling Widgets, feed and cross-device..." 'STEP'

# Widgets (taskbar) off
Set-KairosReg 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh' 'AllowNewsAndInterests' 0 'DWord' 'Disabled Widgets (policy)'
Set-KairosReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'TaskbarDa' 0 'DWord' 'Removed Widgets button from taskbar'

# Kill widget processes now
Get-Process -Name 'Widgets','WidgetBoard','WidgetService' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Kairos "Stopped widget processes" 'OK'

# News feed / MicrosoftStartFeed
Set-KairosReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds' 'EnableFeeds' 0 'DWord' 'Disabled news feed (policy)'

# CrossDeviceResume ("continue on another device")
Get-Process -Name 'CrossDeviceResume' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Set-KairosReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\CrossDevice' 'EnableCrossDeviceResume' 0 'DWord' 'Disabled CrossDeviceResume'

# ============================================================================
#  3. TUNE THE 'ASK' SERVICES  (per Kairos decisions)
# ============================================================================
Write-Host ""
Write-Kairos "Tuning services..." 'STEP'

$Services = @{
    'DoSvc'   = 'Manual'     # Delivery Optimization (P2P updates) - not needed
    'lfsvc'   = 'Disabled'   # Geolocation - user asked to remove location
    'wlidsvc' = 'Manual'     # MS Account sign-in - Manual (Store needs it sometimes)
    'SysMain' = 'Disabled'   # SuperFetch - off on SSD/VM
    # Spooler kept at Manual (printers); WSearch kept (needed until KaiSpot exists)
}
foreach ($svc in $Services.Keys) {
    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($s) {
        try {
            Set-Service -Name $svc -StartupType $Services[$svc] -ErrorAction Stop
            if ($s.Status -eq 'Running' -and $Services[$svc] -eq 'Disabled') {
                Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
            }
            Write-Kairos "Service '$svc' -> $($Services[$svc])" 'OK'
        } catch { Write-Kairos "Could not change '$svc' ($($_.Exception.Message))" 'WARN' }
    } else { Write-Kairos "Service '$svc' not found" 'INFO' }
}

# ============================================================================
#  4. DISABLE TELEMETRY / DIAGNOSTIC SCHEDULED TASKS
# ============================================================================
Write-Host ""
Write-Kairos "Disabling telemetry scheduled tasks..." 'STEP'

$Tasks = @(
    '\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser'
    '\Microsoft\Windows\Application Experience\ProgramDataUpdater'
    '\Microsoft\Windows\Application Experience\StartupAppTask'
    '\Microsoft\Windows\Customer Experience Improvement Program\Consolidator'
    '\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip'
    '\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask'
    '\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector'
    '\Microsoft\Windows\Feedback\Siuf\DmClient'
    '\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload'
    '\Microsoft\Windows\Windows Error Reporting\QueueReporting'
)
foreach ($t in $Tasks) {
    try {
        $task = Get-ScheduledTask -TaskPath ([System.IO.Path]::GetDirectoryName($t) + '\') -TaskName ([System.IO.Path]::GetFileName($t)) -ErrorAction SilentlyContinue
        if ($task) {
            Disable-ScheduledTask -TaskPath ([System.IO.Path]::GetDirectoryName($t) + '\') -TaskName ([System.IO.Path]::GetFileName($t)) -ErrorAction Stop | Out-Null
            Write-Kairos "Disabled task: $([System.IO.Path]::GetFileName($t))" 'OK'
        }
    } catch { Write-Kairos "Task skip: $([System.IO.Path]::GetFileName($t))" 'INFO' }
}

# ============================================================================
#  5. REDUCE DEFENDER  (see Tamper Protection note)
# ============================================================================
Write-Host ""
Write-Kairos "Attempting to reduce Defender..." 'STEP'
Write-Kairos "NOTE: If Tamper Protection is ON, Windows will block this." 'WARN'
Write-Kairos "To fully reduce Defender, disable Tamper Protection manually first" 'WARN'
Write-Kairos "(Windows Security > Virus protection > Manage settings)." 'WARN'

try {
    Set-MpPreference -DisableRealtimeMonitoring $true -ErrorAction Stop
    Write-Kairos "Real-time monitoring disabled." 'OK'
} catch {
    Write-Kairos "Could not disable real-time monitoring (Tamper Protection likely ON)." 'WARN'
}
# Policy-level (applies if Tamper Protection allows)
Set-KairosReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender' 'DisableAntiSpyware' 1 'DWord' 'Set Defender policy to disabled (takes effect if Tamper Protection off)'

Write-Kairos "Defender note: full removal happens at the playbook-imaging stage," 'INFO'
Write-Kairos "where Kairos will substitute its own lightweight protection." 'INFO'

# ============================================================================
#  6. RESTART EXPLORER to apply taskbar/shell changes
# ============================================================================
Write-Host ""
Write-Kairos "Restarting Explorer to apply shell changes..." 'STEP'
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }
Write-Kairos "Explorer restarted." 'OK'

# ============================================================================
#  DONE
# ============================================================================
Write-Host ""
Write-Host "  -------------------------------------" -ForegroundColor DarkGray
Write-Kairos "Deep cleanup complete." 'OK'
Write-Kairos "RESTART the VM, then run Kairos-Audit again to compare." 'INFO'
Write-Kairos "Log: $LogFile" 'INFO'
Write-Host ""
Write-Host "  To undo: restore your VM snapshot." -ForegroundColor DarkGray
Write-Host ""
