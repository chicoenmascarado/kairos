<#
.SYNOPSIS
    Copia todo lo de Kairos a un USB que YA tiene Ventoy instalado. Solo copia
    archivos: no formatea ni toca nada del equipo.

      USB:\Kairos-v0.2.iso (o v0.1)   el sistema
      USB:\KairosSetup\               el post-install (se lanza solo al primer inicio)
      USB:\$WinPEDriver$\             drivers: Windows Setup los instala solo
      USB:\ventoy\                    región, teclado, hora y arranque automático

.EXAMPLE
    .\Preparar-USB.ps1 -Usb E:
#>
param(
    [Parameter(Mandatory)] [string]$Usb,
    [string]$Build = "$env:USERPROFILE\Documents\KairosBuild\KairosSetup",
    [string]$IsoFolder = "$env:USERPROFILE\Downloads"
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Usb = $Usb.TrimEnd('\', ':') + ':'

$vol = Get-Volume -DriveLetter $Usb[0]
if ($vol.FileSystemLabel -ne 'Ventoy') {
    throw "$Usb no parece un USB con Ventoy (etiqueta '$($vol.FileSystemLabel)'). Instala Ventoy primero con Ventoy2Disk."
}

$iso = Get-ChildItem $IsoFolder -Filter 'Kairos-v*.iso' | Sort-Object Name -Descending | Select-Object -First 1
if (-not $iso) { throw "No encuentro Kairos-v*.iso en $IsoFolder" }
$need = $iso.Length + (Get-ChildItem $Build -Recurse -File | Measure-Object Length -Sum).Sum
if ($vol.SizeRemaining -lt $need) { throw ("Falta espacio en el USB: hacen falta {0:N1} GB" -f ($need / 1GB)) }

Write-Host "ISO: $($iso.Name)"
Copy-Item $iso.FullName "$Usb\" -Force

Write-Host 'KairosSetup...'
robocopy $Build "$Usb\KairosSetup" /E /XD drivers /NFL /NDL /NJH /NJS | Out-Null

if (Test-Path "$Build\drivers") {
    Write-Host 'Drivers -> $WinPEDriver$ ...'
    robocopy "$Build\drivers" "$Usb\`$WinPEDriver`$" /E /NFL /NDL /NJH /NJS | Out-Null
} else {
    Write-Warning 'Sin drivers exportados: Windows usará los suyos (el Wi-Fi podría necesitar cable al principio).'
}

Write-Host 'Configuración de Ventoy...'
New-Item -ItemType Directory -Force "$Usb\ventoy" | Out-Null
Copy-Item "$repo\setup\usb\ventoy\*" "$Usb\ventoy\" -Force
Copy-Item "$repo\setup\LEEME.txt" "$Usb\LEEME - Kairos.txt" -Force

Write-Host "USB listo en $Usb" -ForegroundColor Green
