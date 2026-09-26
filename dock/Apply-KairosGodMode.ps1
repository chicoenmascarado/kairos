<#
    Kairos · God Mode  —  maximum-performance Windows power plan.

    RUN AS ADMINISTRATOR inside the VM (right-click → Run with PowerShell, or from
    an elevated PowerShell:  powershell -ExecutionPolicy Bypass -File .\Apply-KairosGodMode.ps1)

    Reversible: switch back any time with   powercfg /setactive SCHEME_BALANCED
#>

$ErrorActionPreference = 'SilentlyContinue'

# Base it on the hidden "Ultimate Performance" template, else "High Performance".
$ULTIMATE = 'e9a42b02-d5df-448d-aa00-03f14749eb61'
$HIGH     = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'

# Ultimate Performance often isn't present (its template error is harmless) → fall
# back to High Performance. 2>$null keeps the console clean.
$out = powercfg -duplicatescheme $ULTIMATE 2>$null
if (-not $out) { $out = powercfg -duplicatescheme $HIGH 2>$null }
$guid = ([regex]'([0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})').Match($out).Value
if (-not $guid) { Write-Host 'No se pudo crear el plan.' -ForegroundColor Red; exit 1 }

powercfg -changename $guid "Kairos - God Mode" "Maximo rendimiento: CPU al 100%, sin throttling ni suspension."

# Processor: lock to 100% (no throttling), active cooling.
powercfg /setacvalueindex $guid SUB_PROCESSOR PROCTHROTTLEMIN 100
powercfg /setdcvalueindex $guid SUB_PROCESSOR PROCTHROTTLEMIN 100
powercfg /setacvalueindex $guid SUB_PROCESSOR PROCTHROTTLEMAX 100
powercfg /setdcvalueindex $guid SUB_PROCESSOR PROCTHROTTLEMAX 100
powercfg /setacvalueindex $guid SUB_PROCESSOR SYSCOOLPOL 1

# No sleep / hibernate / disk spindown / display-off (on AC).
powercfg /setacvalueindex $guid SUB_SLEEP STANDBYIDLE 0
powercfg /setacvalueindex $guid SUB_SLEEP HIBERNATEIDLE 0
powercfg /setacvalueindex $guid SUB_DISK DISKIDLE 0
powercfg /setacvalueindex $guid SUB_VIDEO VIDEOIDLE 0

# Kill latency / power-saving features.
powercfg /setacvalueindex $guid SUB_USB 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0   # USB selective suspend OFF
powercfg /setacvalueindex $guid SUB_PCIEXPRESS ASPM 0                              # PCIe link power mgmt OFF

powercfg /setactive $guid
Write-Host "Plan 'Kairos - God Mode' aplicado y activado." -ForegroundColor Green
powercfg /getactivescheme
