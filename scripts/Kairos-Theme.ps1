<#
.SYNOPSIS
    Kairos Theme - Layer 1 (cosmetic identity).

.DESCRIPTION
    Applies the Kairos visual identity to Windows:
      - Violet accent color (#5B5BF5) across the system
      - Forced dark mode (system + apps)
      - Transparency / glass effects on
      - Wallpaper + lock screen set to the Kairos image
      - Accent on title bars and Start/taskbar
      - Optional: system font swap (off by default, see section 7)
      - Startup sound enabled (drop your own Kairos.wav)

    SAFE / REVERSIBLE. Most changes are per-user registry values.
    Place your wallpaper at C:\Kairos\wallpaper.png before running
    (or edit $Wallpaper below to point to your file).

.NOTES
    Project : Kairos
    Phase   : 2 - Interface (Layer 1: cosmetics)
    Run as  : Administrator
#>

#Requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'
$KairosVersion = '0.1.0-dev'

# ====== CONFIG : wallpaper location =========================================
# The script auto-detects the first image inside this folder.
$WallpaperFolder = "C:\Users\Wolf\Pictures\Wallpaper"
$Wallpaper = $null
if (Test-Path $WallpaperFolder) {
    $img = Get-ChildItem -Path $WallpaperFolder -Include *.png,*.jpg,*.jpeg,*.bmp -File -Recurse -ErrorAction SilentlyContinue |
           Sort-Object Length -Descending | Select-Object -First 1
    if ($img) { $Wallpaper = $img.FullName }
}
# Fallback: if nothing found, you can hardcode the full path here instead:
# $Wallpaper = "C:\Users\Wolf\Pictures\Wallpaper\kairos.png"
# ============================================================================

$LogDir  = "$env:ProgramData\Kairos\logs"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$LogFile = Join-Path $LogDir ("theme_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))

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
        if ($Desc) { Write-Kairos "$Desc" 'OK' }
    } catch { Write-Kairos "Failed: $Desc ($($_.Exception.Message))" 'WARN' }
}

Clear-Host
Write-Host ""
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host "    K A I R O S   -   THEME (Layer 1)" -ForegroundColor Magenta
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host "  Cosmetic identity  -  v$KairosVersion" -ForegroundColor White
Write-Host ""
$confirm = Read-Host "  Type 'KAIROS' to continue"
if ($confirm -ne 'KAIROS') { Write-Kairos "Aborted." 'INFO'; return }

Write-Host ""
if ($Wallpaper) { Write-Kairos "Wallpaper detected: $Wallpaper" 'OK' }
else { Write-Kairos "No wallpaper found in $WallpaperFolder - theme will apply without it." 'WARN' }

# ============================================================================
#  1. DARK MODE (system + apps)
# ============================================================================
Write-Host ""
Write-Kairos "Enabling dark mode..." 'STEP'
$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
Set-KReg $personalize 'AppsUseLightTheme'    0 'DWord' 'Apps set to dark'
Set-KReg $personalize 'SystemUsesLightTheme' 0 'DWord' 'System set to dark'

# ============================================================================
#  2. VIOLET ACCENT COLOR (#5B5BF5)
# ============================================================================
Write-Host ""
Write-Kairos "Applying Kairos violet accent..." 'STEP'
# Windows stores accent as ABGR DWORD. #5B5BF5 -> R=5B G=5B B=F5
# ABGR = FF F5 5B 5B  -> 0xFFF55B5B
$accentDword = 0xFFF55B5B
$dwm = 'HKCU:\Software\Microsoft\Windows\DWM'
Set-KReg $dwm 'AccentColor'            $accentDword 'DWord' 'Set DWM accent color'
Set-KReg $dwm 'ColorizationColor'      $accentDword 'DWord' 'Set colorization color'
Set-KReg $dwm 'ColorizationAfterglow'  $accentDword 'DWord' ''
Set-KReg $dwm 'ColorPrevalence'        1            'DWord' 'Accent on title bars and borders'

# accent palette (Windows expects a set of shades). We write the main one.
$accentPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'
# AccentPalette is a binary blob of 8 shades; we set a Kairos-ish palette.
$palette = [byte[]](
    0x9b,0x9b,0xfb,0x00,  0x7e,0x7e,0xf9,0x00,  0x5b,0x5b,0xf5,0x00,  0x49,0x49,0xc4,0x00,
    0x37,0x37,0x93,0x00,  0x25,0x25,0x62,0x00,  0x13,0x13,0x31,0x00,  0xf5,0x5b,0x5b,0x00)
try {
    if (-not (Test-Path $accentPath)) { New-Item -Path $accentPath -Force | Out-Null }
    Set-ItemProperty -Path $accentPath -Name 'AccentPalette' -Value $palette -Type Binary -ErrorAction Stop
    Set-ItemProperty -Path $accentPath -Name 'StartColorMenu' -Value $accentDword -Type DWord -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $accentPath -Name 'AccentColorMenu' -Value $accentDword -Type DWord -ErrorAction SilentlyContinue
    Write-Kairos "Set accent palette" 'OK'
} catch { Write-Kairos "Accent palette failed ($($_.Exception.Message))" 'WARN' }

# show accent on Start and taskbar
Set-KReg $personalize 'ColorPrevalence' 1 'DWord' 'Accent on Start/taskbar'

# ============================================================================
#  3. TRANSPARENCY / GLASS
# ============================================================================
Write-Host ""
Write-Kairos "Enabling transparency effects..." 'STEP'
Set-KReg $personalize 'EnableTransparency' 1 'DWord' 'Transparency on'

# ============================================================================
#  4. WALLPAPER
# ============================================================================
Write-Host ""
Write-Kairos "Setting wallpaper..." 'STEP'
if (Test-Path $Wallpaper) {
    Set-KReg 'HKCU:\Control Panel\Desktop' 'WallpaperStyle' '10' 'String' 'Wallpaper fill mode'
    Set-KReg 'HKCU:\Control Panel\Desktop' 'TileWallpaper'  '0'  'String' ''
    Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name 'Wallpaper' -Value $Wallpaper -ErrorAction SilentlyContinue
    # apply immediately
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KWall {
  [DllImport("user32.dll", CharSet=CharSet.Auto)]
  public static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);
}
"@ -ErrorAction SilentlyContinue
    [KWall]::SystemParametersInfo(20, 0, $Wallpaper, 3) | Out-Null
    Write-Kairos "Wallpaper applied: $Wallpaper" 'OK'
} else {
    Write-Kairos "Wallpaper not found at $Wallpaper - skipping. Edit \$Wallpaper in the script." 'WARN'
}

# ============================================================================
#  5. LOCK SCREEN (use same Kairos image)
# ============================================================================
Write-Host ""
Write-Kairos "Setting lock screen..." 'STEP'
if (Test-Path $Wallpaper) {
    $lockPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'
    Set-KReg $lockPath 'LockScreenImagePath'   $Wallpaper 'String' 'Lock screen image'
    Set-KReg $lockPath 'LockScreenImageUrl'    $Wallpaper 'String' ''
    Set-KReg $lockPath 'LockScreenImageStatus' 1          'DWord'  'Lock screen locked to Kairos image'
} else {
    Write-Kairos "Skipped lock screen (wallpaper not found)" 'WARN'
}

# ============================================================================
#  6. TASKBAR / SHELL polish (align, clean)
# ============================================================================
Write-Host ""
Write-Kairos "Polishing taskbar..." 'STEP'
$adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
Set-KReg $adv 'TaskbarAl'    1 'DWord' 'Taskbar centered (macOS-like)'   # 1=center 0=left
Set-KReg $adv 'TaskbarDa'    0 'DWord' 'Widgets off'
Set-KReg $adv 'TaskbarMn'    0 'DWord' 'Chat off'
Set-KReg $adv 'ShowTaskViewButton' 0 'DWord' 'Task view button off'
Set-KReg $adv 'HideFileExt'  0 'DWord' 'Show file extensions'

# ============================================================================
#  7. SYSTEM FONT  (OPTIONAL - leave commented unless you want to test)
# ============================================================================
# Write-Host ""
# Write-Kairos "System font swap is available but OFF by default." 'INFO'
# To enable: install your font (e.g. Inter), then set its real name below
# and uncomment this block. Test in VM only - a wrong name = unreadable text.
#
# $fontName = "Inter"
# $fontReg = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts'
# Set-ItemProperty -Path $fontReg -Name 'Segoe UI (TrueType)' -Value '' -ErrorAction SilentlyContinue
# Set-KReg 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontSubstitutes' 'Segoe UI' $fontName 'String' 'Font substituted'

# ============================================================================
#  8. STARTUP SOUND (enable; drop your own Kairos.wav later)
# ============================================================================
Write-Host ""
Write-Kairos "Enabling startup sound..." 'STEP'
Set-KReg 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation' 'DisableStartupSound' 0 'DWord' 'Startup sound enabled'
Set-KReg 'HKCU:\AppEvents\Schemes' '(Default)' '.Current' 'String' ''
Write-Kairos "To use a custom sound, replace the WindowsLogon sound in Control Panel > Sounds with Kairos.wav" 'INFO'

# ============================================================================
#  9. APPLY (restart explorer + refresh)
# ============================================================================
Write-Host ""
Write-Kairos "Applying changes (restarting Explorer)..." 'STEP'
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }

Write-Host ""
Write-Host "  -------------------------------------" -ForegroundColor DarkGray
Write-Kairos "Kairos theme (Layer 1) applied." 'OK'
Write-Kairos "Some changes (accent palette, lock screen) fully show after a reboot." 'INFO'
Write-Kairos "Log: $LogFile" 'INFO'
Write-Host ""
