<#
.SYNOPSIS
    Kairos Post-Install - covers what NTLite Free could not remove.

.DESCRIPTION
    Runs once after installation. Permanently removes OneDrive, disables the
    leftover background components (CrossDeviceResume, etc.), and tightens
    services that survived the image edit.

    This is the "post-install layer" of the Kairos playbook.

    SAFE / REVERSIBLE / TRANSPARENT. Run in the VM first.

.NOTES
    Project : Kairos
    Phase   : 1 - Optimized Base (post-install layer)
    Run as  : Administrator
#>

#Requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'
$KairosVersion = '0.1.0-dev'

$LogDir  = "$env:ProgramData\Kairos\logs"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$LogFile = Join-Path $LogDir ("postinstall_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))

function Write-Kairos {
    param([string]$Message, [ValidateSet('INFO','OK','WARN','ERROR','STEP')] [string]$Level = 'INFO')
    $colors = @{ INFO='Gray'; OK='Green'; WARN='Yellow'; ERROR='Red'; STEP='Cyan' }
    $tag    = @{ INFO='  '; OK='[OK]'; WARN='[!]'; ERROR='[X]'; STEP='==>' }
    Write-Host ("{0} {1}" -f $tag[$Level], $Message) -ForegroundColor $colors[$Level]
    "{0:HH:mm:ss} [{1}] {2}" -f (Get-Date), $Level, $Message | Out-File -FilePath $LogFile -Append -Encoding utf8
}
function Set-KReg {
    param([string]$Path, [string]$Name, $Value, [string]$Type = 'DWord', [string]$Desc)
    try {
        if (-not (Test-Path $Path)) { New-Item -Path $Path -Force | Out-Null }
        Set-ItemProperty -Path $Path -Name $Name -Value $Value -Type $Type -ErrorAction Stop
        Write-Kairos "$Desc" 'OK'
    } catch { Write-Kairos "Failed: $Desc ($($_.Exception.Message))" 'WARN' }
}

Clear-Host
Write-Host ""
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host "    K A I R O S  -  POST-INSTALL LAYER" -ForegroundColor Magenta
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host "  v$KairosVersion" -ForegroundColor White
Write-Host ""
$confirm = Read-Host "  Type 'KAIROS' to continue"
if ($confirm -ne 'KAIROS') { Write-Kairos "Aborted." 'INFO'; return }

Write-Host ""
Write-Kairos "Creating restore point..." 'STEP'
try {
    Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
    Checkpoint-Computer -Description "Kairos PostInstall" -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
    Write-Kairos "Restore point created." 'OK'
} catch { Write-Kairos "No restore point. Rely on VM snapshot." 'WARN' }

# ============================================================================
#  1. REMOVE ONEDRIVE  (the proper, permanent way)
# ============================================================================
Write-Host ""
Write-Kairos "Removing OneDrive permanently..." 'STEP'

# kill it
Get-Process -Name OneDrive,OneDriveSetup -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# run official uninstaller (both bitness paths)
foreach ($p in @("$env:SystemRoot\System32\OneDriveSetup.exe","$env:SystemRoot\SysWOW64\OneDriveSetup.exe")) {
    if (Test-Path $p) {
        Start-Process $p '/uninstall' -Wait -ErrorAction SilentlyContinue
        Write-Kairos "Ran OneDrive uninstaller: $p" 'OK'
    }
}

# remove leftover folders
foreach ($f in @("$env:USERPROFILE\OneDrive","$env:LOCALAPPDATA\Microsoft\OneDrive","$env:PROGRAMDATA\Microsoft OneDrive","$env:SystemDrive\OneDriveTemp")) {
    if (Test-Path $f) { Remove-Item -Path $f -Recurse -Force -ErrorAction SilentlyContinue; Write-Kairos "Removed folder: $f" 'OK' }
}

# remove startup entries
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'OneDrive'      -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'OneDriveSetup' -ErrorAction SilentlyContinue

# block reinstall + remove Explorer sidebar entry
Set-KReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive' 'DisableFileSyncNGSC' 1 'DWord' 'Blocked OneDrive reinstall (policy)'
reg add "HKCR\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}" /f /v "System.IsPinnedToNameSpaceTree" /t REG_DWORD /d 0 2>$null | Out-Null

# stop the per-user setup that keeps relaunching
schtasks /Change /TN "\Microsoft\Windows\OneDrive\OneDrive Standalone Update Task" /DISABLE 2>$null | Out-Null

Write-Kairos "OneDrive removed." 'OK'

# ============================================================================
#  2. STOP LEFTOVER BACKGROUND APPS  (CrossDeviceResume, AppActions, feed)
# ============================================================================
Write-Host ""
Write-Kairos "Disabling leftover background components..." 'STEP'

# CrossDeviceResume (47MB in your audit)
Get-Process -Name 'CrossDeviceResume' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Set-KReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\CrossDevice' 'EnableCrossDeviceResume' 0 'DWord' 'Disabled CrossDeviceResume'

# News & interests / feed (machine-wide policy)
Set-KReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds' 'EnableFeeds' 0 'DWord' 'Disabled news feed'
Set-KReg 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh' 'AllowNewsAndInterests' 0 'DWord' 'Disabled widgets/news (policy)'

# Disable background access for remaining store apps (frees RAM)
Set-KReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications' 'GlobalUserDisabled' 1 'DWord' 'Disabled apps running in background'

# ============================================================================
#  3. TIGHTEN SERVICES THAT SURVIVED
# ============================================================================
Write-Host ""
Write-Kairos "Tuning leftover services..." 'STEP'

$Services = @{
    'DoSvc'        = 'Manual'     # Delivery Optimization
    'SysMain'      = 'Disabled'   # SuperFetch (SSD/VM)
    'DiagTrack'    = 'Disabled'   # Telemetry (may have returned)
    'MapsBroker'   = 'Manual'
    'RetailDemo'   = 'Disabled'
    'lfsvc'        = 'Disabled'   # Location
    'WSearch'      = 'Manual'     # Search - Manual (KaiSpot will replace it later)
}
foreach ($svc in $Services.Keys) {
    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($s) {
        try {
            Set-Service -Name $svc -StartupType $Services[$svc] -ErrorAction Stop
            if ($s.Status -eq 'Running' -and $Services[$svc] -eq 'Disabled') { Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue }
            Write-Kairos "Service '$svc' -> $($Services[$svc])" 'OK'
        } catch { Write-Kairos "Could not change '$svc'" 'WARN' }
    }
}

# ============================================================================
#  4. EXTRA TELEMETRY TASKS  (in case they returned)
# ============================================================================
Write-Host ""
Write-Kairos "Disabling telemetry scheduled tasks..." 'STEP'
$Tasks = @(
    '\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser'
    '\Microsoft\Windows\Application Experience\ProgramDataUpdater'
    '\Microsoft\Windows\Customer Experience Improvement Program\Consolidator'
    '\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip'
    '\Microsoft\Windows\Feedback\Siuf\DmClient'
    '\Microsoft\Windows\Windows Error Reporting\QueueReporting'
)
foreach ($t in $Tasks) {
    $dir = [System.IO.Path]::GetDirectoryName($t) + '\'
    $nm  = [System.IO.Path]::GetFileName($t)
    try {
        if (Get-ScheduledTask -TaskPath $dir -TaskName $nm -ErrorAction SilentlyContinue) {
            Disable-ScheduledTask -TaskPath $dir -TaskName $nm -ErrorAction Stop | Out-Null
            Write-Kairos "Disabled task: $nm" 'OK'
        }
    } catch { Write-Kairos "Task skip: $nm" 'INFO' }
}

# ============================================================================
#  5. RESTART EXPLORER
# ============================================================================
Write-Host ""
Write-Kairos "Restarting Explorer..." 'STEP'
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }
Write-Kairos "Done." 'OK'

# ============================================================================
Write-Host ""
Write-Host "  -------------------------------------" -ForegroundColor DarkGray
Write-Kairos "Post-install layer complete." 'OK'
Write-Kairos "RESTART the VM, wait 3-4 min (let Windows Update settle)," 'INFO'
Write-Kairos "then run Kairos-Audit again to compare." 'INFO'
Write-Host ""
