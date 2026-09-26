<#
    Kairos · Performance Tweaks  —  curated, reversible tweaks for the VM.

    RUN AS ADMINISTRATOR inside the VM:
        powershell -ExecutionPolicy Bypass -File .\Apply-KairosTweaks.ps1

    Everything here is reversible; the original value / how to undo is noted per item.
    These favour responsiveness on a Windows VM. Review before running.
#>

$ErrorActionPreference = 'SilentlyContinue'
function Set-Reg($path, $name, $value, $type='DWord') {
    if (-not (Test-Path $path)) { New-Item -Path $path -Force | Out-Null }
    New-ItemProperty -Path $path -Name $name -Value $value -PropertyType $type -Force | Out-Null
}

Write-Host "Aplicando tweaks de rendimiento Kairos..." -ForegroundColor Cyan

# 1) Favour foreground apps (snappier active window).  Undo: value 2.
Set-Reg 'HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl' 'Win32PrioritySeparation' 0x26

# 2) No reserved CPU for "system" + no network throttling (better for a VM/desktop).
#    Undo: SystemResponsiveness 20 (0x14), NetworkThrottlingIndex 10.
$mm = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'
Set-Reg $mm 'SystemResponsiveness' 0
Set-Reg $mm 'NetworkThrottlingIndex' 0xffffffff

# 3) Visual effects → performance, but keep the bits that still look good.
#    Undo: VisualFXSetting 0 (let Windows decide) or 1 (best appearance).
Set-Reg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' 'VisualFXSetting' 2

# 4) Turn off Game DVR / Game Bar background capture (frees CPU/GPU).
#    Undo: AppCaptureEnabled 1, GameDVR_Enabled 1.
Set-Reg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' 'AppCaptureEnabled' 0
Set-Reg 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled' 0

# 5) Disable hibernation (saves disk; useless in a VM).  Undo: powercfg /h on
powercfg /h off 2>$null

# 6) Disable SysMain (Superfetch) — little benefit on SSD/VM, frees I/O.
#    Undo: Set-Service SysMain -StartupType Automatic; Start-Service SysMain
Stop-Service SysMain -Force 2>$null
Set-Service SysMain -StartupType Disabled 2>$null

# 7) Disable Windows Search indexing service (optional; saves CPU/disk on a VM).
#    Undo: Set-Service WSearch -StartupType Automatic; Start-Service WSearch
Stop-Service WSearch -Force 2>$null
Set-Service WSearch -StartupType Disabled 2>$null

Write-Host "Listo. Reinicia la VM para que todo surta efecto." -ForegroundColor Green
Write-Host "Para revertir, consulta los comentarios 'Undo' de este script." -ForegroundColor DarkGray
