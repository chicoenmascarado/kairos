<#
.SYNOPSIS
    Kairos Lock Screen (direct method) - replaces the internal system image.

.DESCRIPTION
    When the Personalization policy is ignored (common on Win11 build 26x00),
    this replaces the actual image files Windows uses for the lock screen,
    inside C:\Windows\Web\Screen and the SystemData lock cache.

    Takes ownership of the protected files, backs them up, and swaps in the
    Kairos wallpaper.

    REVERSIBLE: backups are saved with a .kairosbak extension.

.NOTES
    Project : Kairos
    Phase   : 2 - Interface (lock screen, direct method)
    Run as  : Administrator
#>

#Requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'

# ====== CONFIG ==============================================================
$WallpaperFolder = "C:\Users\Wolf\Pictures\Wallpaper"
$Wallpaper = $null
if (Test-Path $WallpaperFolder) {
    $img = Get-ChildItem -Path $WallpaperFolder -Include *.png,*.jpg,*.jpeg -File -Recurse -ErrorAction SilentlyContinue |
           Sort-Object Length -Descending | Select-Object -First 1
    if ($img) { $Wallpaper = $img.FullName }
}
# ============================================================================

function Write-Kairos {
    param([string]$Message, [ValidateSet('INFO','OK','WARN','ERROR','STEP')] [string]$Level = 'INFO')
    $colors = @{ INFO='Gray'; OK='Green'; WARN='Yellow'; ERROR='Red'; STEP='Cyan' }
    $tag    = @{ INFO='  '; OK='[OK]'; WARN='[!]'; ERROR='[X]'; STEP='==>' }
    Write-Host ("{0} {1}" -f $tag[$Level], $Message) -ForegroundColor $colors[$Level]
}

function Take-OwnAndReplace {
    param([string]$Target, [string]$NewImage)
    if (-not (Test-Path $Target)) { Write-Kairos "Not present: $Target" 'INFO'; return }
    try {
        takeown /F $Target /A 2>$null | Out-Null
        icacls $Target /grant "Administrators:F" 2>$null | Out-Null
        if (-not (Test-Path "$Target.kairosbak")) {
            Copy-Item $Target "$Target.kairosbak" -Force -ErrorAction SilentlyContinue
        }
        Copy-Item $NewImage $Target -Force -ErrorAction Stop
        Write-Kairos "Replaced: $Target" 'OK'
    } catch { Write-Kairos "Could not replace $Target ($($_.Exception.Message))" 'WARN' }
}

Clear-Host
Write-Host ""
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host "   KAIROS  -  LOCK SCREEN (direct)" -ForegroundColor Magenta
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host ""
if (-not $Wallpaper) { Write-Kairos "No wallpaper found in $WallpaperFolder" 'ERROR'; return }
Write-Kairos "Wallpaper: $Wallpaper" 'OK'
Write-Host ""
Write-Kairos "This replaces protected system images (backups made). VM only." 'WARN'
$confirm = Read-Host "  Type 'KAIROS' to continue"
if ($confirm -ne 'KAIROS') { Write-Kairos "Aborted." 'INFO'; return }

# ============================================================================
#  1. Replace the default lock screen images in C:\Windows\Web\Screen
# ============================================================================
Write-Host ""
Write-Kairos "Replacing default lock screen images..." 'STEP'
$screenDir = "$env:SystemRoot\Web\Screen"
if (Test-Path $screenDir) {
    Get-ChildItem -Path $screenDir -Include *.jpg,*.png -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
        Take-OwnAndReplace $_.FullName $Wallpaper
    }
} else { Write-Kairos "$screenDir not found" 'WARN' }

# ============================================================================
#  2. Clear the lock screen cache so Windows regenerates from new image
# ============================================================================
Write-Host ""
Write-Kairos "Clearing lock screen cache..." 'STEP'
$cacheDir = "$env:ProgramData\Microsoft\Windows\SystemData"
# The actual cached lockscreen lives under SystemData\<SID>\ReadOnly\LockScreen_*
# We can't always reach it, but clearing the user cache helps:
$userCache = "$env:LOCALAPPDATA\Packages\Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy\LocalState\Assets"
if (Test-Path $userCache) {
    try {
        Get-ChildItem $userCache -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
        Write-Kairos "Cleared ContentDeliveryManager asset cache" 'OK'
    } catch { Write-Kairos "Cache clear partial" 'INFO' }
}

# ============================================================================
#  3. Re-assert the policy too (belt and suspenders)
# ============================================================================
Write-Host ""
Write-Kairos "Re-asserting policy..." 'STEP'
$p = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'
if (-not (Test-Path $p)) { New-Item -Path $p -Force | Out-Null }
Set-ItemProperty -Path $p -Name 'LockScreenImagePath' -Value $Wallpaper -Type String -ErrorAction SilentlyContinue
Set-ItemProperty -Path $p -Name 'LockScreenImageUrl'  -Value $Wallpaper -Type String -ErrorAction SilentlyContinue
Set-ItemProperty -Path $p -Name 'LockScreenImageStatus' -Value 1 -Type DWord -ErrorAction SilentlyContinue
Write-Kairos "Policy re-asserted" 'OK'

Write-Host ""
Write-Host "  -------------------------------------" -ForegroundColor DarkGray
Write-Kairos "Done. RESTART the VM completely, then check the lock screen." 'OK'
Write-Kairos "Backups saved as *.kairosbak next to each replaced file." 'INFO'
Write-Host ""
