<#
.SYNOPSIS
    Kairos Lock & Login - polishes the lock screen and sign-in experience.

.DESCRIPTION
    - Sets lock screen + login background to the Kairos wallpaper
    - Removes the acrylic/blur on the sign-in screen (crisp wallpaper)
    - Disables Spotlight/tips/ads and "fun facts" on the lock screen
    - Optional welcome message on sign-in
    - Keeps accent violet (inherited from theme)

    What Windows does NOT allow without replacing system components:
    custom clock font, custom login layout, custom animations. Those are
    out of scope here.

    SAFE / REVERSIBLE.

.NOTES
    Project : Kairos
    Phase   : 2 - Interface (lock/login polish)
    Run as  : Administrator
#>

#Requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'

# ====== CONFIG ==============================================================
$WallpaperFolder = "C:\Users\Wolf\Pictures\Wallpaper"
$Wallpaper = $null
if (Test-Path $WallpaperFolder) {
    $img = Get-ChildItem -Path $WallpaperFolder -Include *.png,*.jpg,*.jpeg,*.bmp -File -Recurse -ErrorAction SilentlyContinue |
           Sort-Object Length -Descending | Select-Object -First 1
    if ($img) { $Wallpaper = $img.FullName }
}
# Welcome message shown on the sign-in screen (set to "" to disable)
# Left empty by default: a pre-login dialog adds an extra click and breaks the
# clean, fast sign-in flow. Set a title/text here only if you really want it.
$WelcomeTitle = ""
$WelcomeText  = ""
# ============================================================================

$LogDir  = "$env:ProgramData\Kairos\logs"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$LogFile = Join-Path $LogDir ("locklogin_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))

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
Write-Host "    K A I R O S  -  LOCK & LOGIN" -ForegroundColor Magenta
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host ""
if ($Wallpaper) { Write-Kairos "Wallpaper: $Wallpaper" 'OK' }
else { Write-Kairos "No wallpaper found in $WallpaperFolder" 'WARN' }
Write-Host ""
$confirm = Read-Host "  Type 'KAIROS' to continue"
if ($confirm -ne 'KAIROS') { Write-Kairos "Aborted." 'INFO'; return }

# ============================================================================
#  1. LOCK SCREEN IMAGE (lock to Kairos wallpaper)
# ============================================================================
Write-Host ""
Write-Kairos "Setting lock screen image..." 'STEP'
if ($Wallpaper) {
    $personalization = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'
    Set-KReg $personalization 'LockScreenImagePath'   $Wallpaper 'String' 'Lock screen image set'
    Set-KReg $personalization 'LockScreenImageUrl'    $Wallpaper 'String' ''
    Set-KReg $personalization 'LockScreenImageStatus' 1          'DWord'  'Lock screen locked to Kairos image'
} else { Write-Kairos "Skipped (no wallpaper)" 'WARN' }

# ============================================================================
#  2. DISABLE SPOTLIGHT / TIPS / ADS ON LOCK SCREEN
# ============================================================================
Write-Host ""
Write-Kairos "Removing lock screen ads, tips and Spotlight..." 'STEP'
$cdm = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
Set-KReg $cdm 'RotatingLockScreenEnabled'        0 'DWord' 'Disabled rotating lock screen'
Set-KReg $cdm 'RotatingLockScreenOverlayEnabled' 0 'DWord' 'Disabled lock screen overlay ads'
Set-KReg $cdm 'SubscribedContent-338387Enabled'  0 'DWord' 'Disabled lock screen fun facts/tips'
# policy-level Spotlight off
Set-KReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableWindowsSpotlightFeatures' 1 'DWord' 'Disabled Windows Spotlight (policy)'

# ============================================================================
#  3. CRISP SIGN-IN BACKGROUND (remove acrylic blur)
# ============================================================================
Write-Host ""
Write-Kairos "Making sign-in background crisp (no blur)..." 'STEP'
Set-KReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'DisableAcrylicBackgroundOnLogon' 1 'DWord' 'Disabled login blur (crisp wallpaper)'

# ============================================================================
#  4. SHOW LOCK SCREEN BACKGROUND ON SIGN-IN (use same image)
# ============================================================================
Write-Host ""
Write-Kairos "Using Kairos image on sign-in screen..." 'STEP'
# 0 = show lock screen background on sign-in (uses the Kairos image)
Set-KReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'DisableLogonBackgroundImage' 0 'DWord' 'Sign-in uses Kairos background'

# ============================================================================
#  5. WELCOME MESSAGE ON SIGN-IN (optional)
# ============================================================================
Write-Host ""
if ($WelcomeTitle -ne "") {
    Write-Kairos "Setting sign-in welcome message..." 'STEP'
    $policySystem = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
    Set-KReg $policySystem 'legalnoticecaption' $WelcomeTitle 'String' "Welcome title: $WelcomeTitle"
    Set-KReg $policySystem 'legalnoticetext'    $WelcomeText  'String' "Welcome text set"
    Write-Kairos "Note: this shows as a dialog before sign-in. Remove by clearing those values." 'INFO'
} else {
    Write-Kairos "Welcome message disabled (empty)." 'INFO'
}

# ============================================================================
#  DONE
# ============================================================================
Write-Host ""
Write-Host "  -------------------------------------" -ForegroundColor DarkGray
Write-Kairos "Lock & login polish applied." 'OK'
Write-Kairos "LOCK the screen (Win+L) or sign out to see the changes." 'INFO'
Write-Kairos "Log: $LogFile" 'INFO'
Write-Host ""
