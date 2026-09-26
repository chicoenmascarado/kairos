<#
.SYNOPSIS
    Kairos Audit - READ ONLY system inventory.

.DESCRIPTION
    Inventories running services, startup apps, and processes, and explains what
    each one does. CHANGES NOTHING. 100% safe to run anywhere.

    Produces:
      - On-screen summary
      - A detailed report file (TXT) you can share back for review
      - A CSV of services for easy analysis

.NOTES
    Project : Kairos
    Phase   : 1 - Optimized Base (audit step)
    Run as  : Administrator (for full detail)
    Safe    : YES - read only, makes no changes.
#>

#Requires -RunAsAdministrator

$KairosVersion = '0.1.0-dev'
$OutDir = "$env:USERPROFILE\Desktop\Kairos-Audit"
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$Stamp   = '{0:yyyyMMdd_HHmmss}' -f (Get-Date)
$Report  = Join-Path $OutDir "audit_$Stamp.txt"
$CsvFile = Join-Path $OutDir "services_$Stamp.csv"

function Line { param($t); Write-Host $t; $t | Out-File -FilePath $Report -Append -Encoding utf8 }

# --- known service descriptions (plain language, Kairos recommendation) -----
# Recommendation legend:
#   KEEP    = core, do not touch
#   MANUAL  = safe to set to Manual (starts only if needed)
#   DISABLE = safe to disable for most users
#   ASK     = depends on user's use case - decide together
$Known = @{
  'DiagTrack'             = @('DISABLE','Telemetry / data collection')
  'dmwappushservice'      = @('DISABLE','WAP push routing (telemetry related)')
  'RetailDemo'            = @('DISABLE','Store demo mode - never needed at home')
  'Fax'                   = @('DISABLE','Fax service')
  'MapsBroker'            = @('MANUAL','Offline maps - Manual is fine')
  'WMPNetworkSvc'         = @('MANUAL','Windows Media Player network sharing')
  'XblAuthManager'        = @('ASK','Xbox Live auth - KEEP if you game')
  'XblGameSave'           = @('ASK','Xbox game save - KEEP if you game')
  'XboxNetApiSvc'         = @('ASK','Xbox networking - KEEP if you game')
  'XboxGipSvc'            = @('ASK','Xbox accessory management - KEEP if you game')
  'OneSyncSvc'            = @('ASK','Sync mail/contacts/calendar for MS accounts')
  'wlidsvc'               = @('ASK','Microsoft Account sign-in assistant')
  'lfsvc'                 = @('ASK','Geolocation / location service')
  'WSearch'               = @('ASK','Windows Search indexing - heavy but useful')
  'SysMain'               = @('ASK','SuperFetch/prefetch - debated on SSDs')
  'DPS'                   = @('KEEP','Diagnostic Policy - leave alone')
  'PcaSvc'                = @('MANUAL','Program Compatibility Assistant')
  'WerSvc'                = @('MANUAL','Windows Error Reporting')
  'Spooler'               = @('ASK','Print Spooler - DISABLE if no printer')
  'TabletInputService'    = @('ASK','Touch keyboard / handwriting - KEEP on touch')
  'TouchKeyboard'         = @('ASK','Touch keyboard')
  'BthAvctpSvc'           = @('ASK','Bluetooth audio/video - KEEP if you use BT')
  'bthserv'               = @('ASK','Bluetooth support - KEEP if you use BT')
  'WpcMonSvc'             = @('MANUAL','Parental controls')
  'SCardSvr'              = @('MANUAL','Smart card')
  'ScDeviceEnum'          = @('MANUAL','Smart card enumeration')
  'SEMgrSvc'              = @('MANUAL','NFC / payments')
  'PhoneSvc'              = @('MANUAL','Phone telephony service')
  'SharedAccess'          = @('MANUAL','Internet Connection Sharing')
  'RemoteRegistry'        = @('DISABLE','Remote registry - security risk, off by default')
  'RemoteAccess'          = @('MANUAL','Routing and Remote Access')
  'SessionEnv'            = @('MANUAL','Remote Desktop config')
  'TermService'           = @('ASK','Remote Desktop - KEEP if you use RDP')
  'WbioSrvc'              = @('ASK','Biometrics / Windows Hello - KEEP if used')
  'wisvc'                 = @('MANUAL','Windows Insider')
  'WPDBusEnum'            = @('MANUAL','Portable device enumeration')
  'DusmSvc'               = @('KEEP','Data usage - light, leave alone')
  'DoSvc'                 = @('ASK','Delivery Optimization - P2P update sharing')
  'MicrosoftEdgeElevationService' = @('MANUAL','Edge updater elevation')
  'edgeupdate'            = @('MANUAL','Edge update')
  'edgeupdatem'           = @('MANUAL','Edge update (machine)')
  'GoogleUpdaterService'  = @('MANUAL','Google updater (if Chrome installed)')
  'gupdate'               = @('MANUAL','Google update')
  'gupdatem'              = @('MANUAL','Google update (machine)')
}

# --- banner ----------------------------------------------------------------
Clear-Host
Write-Host ""
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host "         K A I R O S   A U D I T" -ForegroundColor Magenta
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host "  READ ONLY - nothing will be changed." -ForegroundColor DarkGray
Write-Host ""

Line "KAIROS SYSTEM AUDIT  -  v$KairosVersion"
Line ("Generated: {0}" -f (Get-Date))
Line ("Machine:   {0}" -f $env:COMPUTERNAME)
Line "============================================================"
Line ""

# --- system snapshot -------------------------------------------------------
$os   = Get-CimInstance Win32_OperatingSystem
$proc = (Get-Process).Count
$svcRunning = (Get-Service | Where-Object Status -EQ 'Running').Count
$svcTotal   = (Get-Service).Count

Line "SUMMARY"
Line "-------"
Line ("Windows           : {0} (build {1})" -f $os.Caption, $os.BuildNumber)
Line ("RAM in use        : {0:N0} MB of {1:N0} MB" -f (($os.TotalVisibleMemorySize-$os.FreePhysicalMemory)/1KB), ($os.TotalVisibleMemorySize/1KB))
Line ("Running processes : {0}" -f $proc)
Line ("Running services  : {0} (of {1} total)" -f $svcRunning, $svcTotal)
Line ""

Write-Host ("  Running processes : {0}" -f $proc) -ForegroundColor Cyan
Write-Host ("  Running services  : {0} of {1}" -f $svcRunning, $svcTotal) -ForegroundColor Cyan
Write-Host ""

# --- RUNNING services with recommendations ---------------------------------
Line "RUNNING SERVICES  (with Kairos recommendation)"
Line "----------------------------------------------"
Line "LEGEND: KEEP=core  MANUAL=on-demand ok  DISABLE=safe to turn off  ASK=depends on you"
Line ""

$svcData = @()
Get-Service | Where-Object Status -EQ 'Running' | Sort-Object DisplayName | ForEach-Object {
    $name = $_.Name
    $rec  = if ($Known.ContainsKey($name)) { $Known[$name][0] } else { 'REVIEW' }
    $desc = if ($Known.ContainsKey($name)) { $Known[$name][1] } else { '(needs review - not in Kairos DB yet)' }
    Line ("[{0,-7}] {1,-28} {2}" -f $rec, $name, $desc)
    Line ("           full name: {0}" -f $_.DisplayName)
    $svcData += [pscustomobject]@{
        Name        = $name
        DisplayName = $_.DisplayName
        Status      = $_.Status
        StartType   = (Get-Service $name).StartType
        Recommend   = $rec
        WhatItIs    = $desc
    }
}
Line ""

# --- STARTUP apps ----------------------------------------------------------
Line "STARTUP APPS  (launch automatically at login)"
Line "---------------------------------------------"
try {
    $startup = Get-CimInstance Win32_StartupCommand
    if ($startup) {
        foreach ($s in $startup) { Line ("  {0,-30} -> {1}" -f $s.Name, $s.Command) }
    } else { Line "  (none found via WMI)" }
} catch { Line "  (could not read startup items)" }
Line ""

# --- TOP processes by RAM --------------------------------------------------
Line "TOP 25 PROCESSES BY MEMORY"
Line "--------------------------"
Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 25 | ForEach-Object {
    Line ("  {0,-32} {1,8:N0} MB" -f $_.ProcessName, ($_.WorkingSet64/1MB))
}
Line ""

# --- ALL processes grouped (count instances + total RAM each) --------------
Line "ALL PROCESSES  (grouped by name: instances + total RAM)"
Line "-------------------------------------------------------"
Get-Process | Group-Object ProcessName | ForEach-Object {
    [pscustomobject]@{
        Name      = $_.Name
        Instances = $_.Count
        TotalMB   = [math]::Round((($_.Group | Measure-Object WorkingSet64 -Sum).Sum / 1MB), 0)
    }
} | Sort-Object TotalMB -Descending | ForEach-Object {
    Line ("  {0,-32} x{1,-3} {2,8:N0} MB" -f $_.Name, $_.Instances, $_.TotalMB)
}
Line ""
Line ("DISTINCT process names: {0}   |   TOTAL process instances: {1}" -f `
    ((Get-Process | Group-Object ProcessName).Count), ((Get-Process).Count))
Line ""

# --- export CSV ------------------------------------------------------------
$svcData | Export-Csv -Path $CsvFile -NoTypeInformation -Encoding UTF8

Line "============================================================"
Line "End of audit. No changes were made."

Write-Host ""
Write-Host "  =====================================" -ForegroundColor Green
Write-Host "  Audit complete. Nothing was changed." -ForegroundColor Green
Write-Host "  =====================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Report : $Report" -ForegroundColor White
Write-Host "  CSV    : $CsvFile" -ForegroundColor White
Write-Host ""
Write-Host "  Send me the .txt report and we'll decide what to cut." -ForegroundColor DarkGray
Write-Host ""
