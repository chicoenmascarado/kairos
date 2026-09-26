<#
.SYNOPSIS
    Kairos Post-Install v2 - aggressive removal of stubborn leftovers.

.DESCRIPTION
    Targets the two survivors from v1:
      - OneDriveSetup (re-injects from System32/SysWOW64)
      - CrossDeviceResume (registry key didn't catch on build 26200)
    Plus re-applies the v1 cleanup so you can run this one standalone.

    More aggressive: removes OneDriveSetup.exe from System image folders and
    disables the CrossDevice package/tasks directly.

    VM ONLY. Snapshot first.

.NOTES
    Project : Kairos
    Phase   : 1 - Optimized Base (post-install v2)
    Run as  : Administrator
#>

#Requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'
$KairosVersion = '0.1.0-dev'

$LogDir  = "$env:ProgramData\Kairos\logs"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$LogFile = Join-Path $LogDir ("postinstall2_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))

function Write-Kairos {
    param([string]$Message, [ValidateSet('INFO','OK','WARN','ERROR','STEP')] [string]$Level = 'INFO')
    $colors = @{ INFO='Gray'; OK='Green'; WARN='Yellow'; ERROR='Red'; STEP='Cyan' }
    $tag    = @{ INFO='  '; OK='[OK]'; WARN='[!]'; ERROR='[X]'; STEP='==>' }
    Write-Host ("{0} {1}" -f $tag[$Level], $Message) -ForegroundColor $colors[$Level]
    "{0:HH:mm:ss} [{1}] {2}" -f (Get-Date), $Level, $Message | Out-File -FilePath $LogFile -Append -Encoding utf8
}

Clear-Host
Write-Host ""
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host "   K A I R O S  -  POST-INSTALL v2" -ForegroundColor Magenta
Write-Host "   (aggressive leftover removal)" -ForegroundColor Magenta
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host ""
Write-Kairos "Removes OneDriveSetup.exe and kills CrossDeviceResume." 'WARN'
Write-Kairos "VM ONLY. Make sure you have a snapshot." 'WARN'
Write-Host ""
$confirm = Read-Host "  Type 'KAIROS' to continue"
if ($confirm -ne 'KAIROS') { Write-Kairos "Aborted." 'INFO'; return }

# ============================================================================
#  1. ONEDRIVE - the nuclear option
# ============================================================================
Write-Host ""
Write-Kairos "Eliminating OneDrive completely..." 'STEP'

# kill processes
Get-Process -Name OneDrive,OneDriveSetup -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# uninstall via official setup first
foreach ($p in @("$env:SystemRoot\System32\OneDriveSetup.exe","$env:SystemRoot\SysWOW64\OneDriveSetup.exe")) {
    if (Test-Path $p) { Start-Process $p '/uninstall' -Wait -ErrorAction SilentlyContinue }
}
Start-Sleep -Seconds 2

# take ownership and DELETE the setup stubs so they cannot re-inject
foreach ($p in @("$env:SystemRoot\System32\OneDriveSetup.exe","$env:SystemRoot\SysWOW64\OneDriveSetup.exe")) {
    if (Test-Path $p) {
        try {
            takeown /F $p /A 2>$null | Out-Null
            icacls $p /grant "Administrators:F" 2>$null | Out-Null
            Remove-Item -Path $p -Force -ErrorAction Stop
            Write-Kairos "Deleted: $p" 'OK'
        } catch { Write-Kairos "Could not delete $p ($($_.Exception.Message))" 'WARN' }
    } else { Write-Kairos "Already gone: $p" 'INFO' }
}

# remove leftover folders
foreach ($f in @("$env:USERPROFILE\OneDrive","$env:LOCALAPPDATA\Microsoft\OneDrive","$env:PROGRAMDATA\Microsoft OneDrive","$env:SystemDrive\OneDriveTemp")) {
    if (Test-Path $f) { Remove-Item -Path $f -Recurse -Force -ErrorAction SilentlyContinue; Write-Kairos "Removed: $f" 'OK' }
}

# remove the "first setup" run keys (both Run and RunOnce, HKLM and HKCU)
foreach ($hive in @('HKCU:','HKLM:')) {
    foreach ($key in @('Run','RunOnce')) {
        $path = "$hive\Software\Microsoft\Windows\CurrentVersion\$key"
        foreach ($name in @('OneDrive','OneDriveSetup')) {
            Remove-ItemProperty -Path $path -Name $name -ErrorAction SilentlyContinue
        }
    }
}

# remove the active setup that re-triggers OneDriveSetup at logon
$activeSetup = 'HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\{2D46B6DC-2207-486B-B523-A557E6D54B47}'
if (Test-Path $activeSetup) {
    Remove-Item -Path $activeSetup -Recurse -Force -ErrorAction SilentlyContinue
    Write-Kairos "Removed OneDrive Active Setup trigger" 'OK'
}

# block via policy
if (-not (Test-Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive')) { New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive' -Force | Out-Null }
Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive' -Name 'DisableFileSyncNGSC' -Value 1 -Type DWord -ErrorAction SilentlyContinue

Write-Kairos "OneDrive fully removed." 'OK'

# ============================================================================
#  2. CROSSDEVICERESUME - kill it for real
# ============================================================================
Write-Host ""
Write-Kairos "Killing CrossDeviceResume..." 'STEP'

# stop the process
Get-Process -Name 'CrossDeviceResume' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# it is part of the CrossDevice package - try removing the Appx package
$pkg = Get-AppxPackage -AllUsers '*CrossDevice*' -ErrorAction SilentlyContinue
if ($pkg) {
    $pkg | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue
    Get-AppxProvisionedPackage -Online | Where-Object DisplayName -like '*CrossDevice*' | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Out-Null
    Write-Kairos "Removed CrossDevice package" 'OK'
} else {
    Write-Kairos "CrossDevice package not found as Appx (trying registry)" 'INFO'
}

# disable via multiple registry locations (build 26200 variants)
foreach ($path in @(
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\CrossDevice',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CrossDevice',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\CrossDeviceResume'
)) {
    if (-not (Test-Path $path)) { New-Item -Path $path -Force -ErrorAction SilentlyContinue | Out-Null }
    Set-ItemProperty -Path $path -Name 'EnableCrossDeviceResume' -Value 0 -Type DWord -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $path -Name 'IsResumeAllowed' -Value 0 -Type DWord -ErrorAction SilentlyContinue
}

# disable any related scheduled task
Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like '*CrossDevice*' } | ForEach-Object {
    Disable-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath -ErrorAction SilentlyContinue | Out-Null
    Write-Kairos "Disabled task: $($_.TaskName)" 'OK'
}

Write-Kairos "CrossDeviceResume handled." 'OK'

# ============================================================================
#  3. RE-ASSERT key cleanup (idempotent)
# ============================================================================
Write-Host ""
Write-Kairos "Re-asserting service tuning..." 'STEP'
$Services = @{ 'DoSvc'='Manual'; 'SysMain'='Disabled'; 'DiagTrack'='Disabled'; 'lfsvc'='Disabled' }
foreach ($svc in $Services.Keys) {
    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($s) {
        Set-Service -Name $svc -StartupType $Services[$svc] -ErrorAction SilentlyContinue
        if ($s.Status -eq 'Running' -and $Services[$svc] -eq 'Disabled') { Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue }
        Write-Kairos "Service '$svc' -> $($Services[$svc])" 'OK'
    }
}

# ============================================================================
#  4. RESTART EXPLORER
# ============================================================================
Write-Host ""
Write-Kairos "Restarting Explorer..." 'STEP'
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }

Write-Host ""
Write-Host "  -------------------------------------" -ForegroundColor DarkGray
Write-Kairos "Post-install v2 complete." 'OK'
Write-Kairos "RESTART the VM completely, wait a few minutes, then re-audit." 'INFO'
Write-Kairos "OneDriveSetup and CrossDeviceResume should now be gone." 'INFO'
Write-Host ""
