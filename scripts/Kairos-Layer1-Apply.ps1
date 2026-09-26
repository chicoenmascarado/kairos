<#
.SYNOPSIS
    Kairos Theme - Layer 1 materials applier (icons, cursors, font, sound).

.DESCRIPTION
    Applies the remaining Layer 1 cosmetic materials:
      1. System icons (folder, recycle bin, drive) from Kairos .ico files
      2. Cursors (your downloaded pack)
      3. System font (Inter) - OPTIONAL, off by default (see $ApplyFont)
      4. Startup sound (Kairos-Startup.wav)

    Put the materials in C:\Kairos\Theme\ (the script tells you where), or edit
    the paths in CONFIG below.

    SAFE / REVERSIBLE. Run as Administrator. VM first.

.NOTES
    Project : Kairos
    Phase   : 2 - Interface (Layer 1 materials)
#>

#Requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'

# ====== CONFIG ==============================================================
# Single drop folder on the Desktop: put EVERYTHING here.
#   Desktop\Kairos\
#     folder.ico  trash-empty.ico  trash-full.ico  drive.ico
#     Kairos-Startup.wav
#     (cursor pack folder, or its .cur/.ani/.inf files)
#     (Inter .ttf files, only if you enable the font)
# Subfolders are fine too - the script searches recursively.
$ThemeDir = "$env:USERPROFILE\Desktop\Kairos"

# Helper: find a file by name anywhere under $ThemeDir (first match), else "".
function Find-Material([string]$pattern) {
    $hit = Get-ChildItem -Path $ThemeDir -Filter $pattern -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($hit) { return $hit.FullName } else { return "" }
}

# Icons - auto-located inside the Kairos folder (loose or in subfolders)
$IconsDir      = $ThemeDir
$IconFolder    = Find-Material 'folder.ico'
$IconTrashEmpty= Find-Material 'trash-empty.ico'
$IconTrashFull = Find-Material 'trash-full.ico'
$IconDrive     = Find-Material 'drive.ico'

# Cursors: use the pack inside the Kairos folder if present, else fall back to Downloads.
$CursorDir = $ThemeDir
if (-not (Get-ChildItem -Path $ThemeDir -Include *.cur,*.ani,*.inf -Recurse -File -ErrorAction SilentlyContinue)) {
    $CursorDir = "$env:USERPROFILE\Downloads\windows_11_cursors_concept_by_jepricreations_densjkc"
}

# Font: drop Inter .ttf in the Kairos folder (download from rsms.me/inter)
$FontDir = $ThemeDir
$ApplyFont = $false                    # set $true to swap system font to Inter
$FontFamilyName = "Inter"              # the family name as Windows sees it

# Startup sound: Kairos-Startup.wav inside the Kairos folder
$StartupWav = Find-Material 'Kairos-Startup.wav'
# ============================================================================

$LogDir = "$env:ProgramData\Kairos\logs"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$LogFile = Join-Path $LogDir ("layer1_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))

function Write-Kairos {
    param([string]$Message, [ValidateSet('INFO','OK','WARN','ERROR','STEP')] [string]$Level = 'INFO')
    $colors = @{ INFO='Gray'; OK='Green'; WARN='Yellow'; ERROR='Red'; STEP='Cyan' }
    $tag    = @{ INFO='  '; OK='[OK]'; WARN='[!]'; ERROR='[X]'; STEP='==>' }
    Write-Host ("{0} {1}" -f $tag[$Level], $Message) -ForegroundColor $colors[$Level]
    "{0:HH:mm:ss} [{1}] {2}" -f (Get-Date), $Level, $Message | Out-File -FilePath $LogFile -Append -Encoding utf8
}
function Set-KReg {
    param([string]$Path, [string]$Name, $Value, [string]$Type = 'String', [string]$Desc)
    try {
        if (-not (Test-Path $Path)) { New-Item -Path $Path -Force | Out-Null }
        Set-ItemProperty -Path $Path -Name $Name -Value $Value -Type $Type -ErrorAction Stop
        if ($Desc) { Write-Kairos $Desc 'OK' }
    } catch { Write-Kairos "Failed: $Desc ($($_.Exception.Message))" 'WARN' }
}

Clear-Host
Write-Host ""
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host "    K A I R O S  -  LAYER 1 MATERIALS" -ForegroundColor Magenta
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host ""
Write-Kairos "Materials expected in: $ThemeDir" 'INFO'
Write-Host ""
$confirm = Read-Host "  Type 'KAIROS' to continue"
if ($confirm -ne 'KAIROS') { Write-Kairos "Aborted." 'INFO'; return }

# ============================================================================
#  1. SYSTEM ICONS
# ============================================================================
Write-Host ""
Write-Kairos "Applying system icons..." 'STEP'

# 1a. Recycle Bin (empty + full)
if ((Test-Path $IconTrashEmpty) -and (Test-Path $IconTrashFull)) {
    $rbClsid = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\DefaultIcon'
    if (-not (Test-Path $rbClsid)) { New-Item -Path $rbClsid -Force | Out-Null }
    Set-KReg $rbClsid '(default)' $IconTrashEmpty 'String' 'Recycle bin (default) icon set'
    Set-KReg $rbClsid 'empty'     $IconTrashEmpty 'String' 'Recycle bin empty icon set'
    Set-KReg $rbClsid 'full'      $IconTrashFull  'String' 'Recycle bin full icon set'
} else { Write-Kairos "Recycle bin icons not found in $IconsDir - skipping" 'WARN' }

# 1b. Default folder icon (Explorer shell icons override)
if (Test-Path $IconFolder) {
    $shellIcons = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons'
    if (-not (Test-Path $shellIcons)) { New-Item -Path $shellIcons -Force | Out-Null }
    # icon index 3 = default closed folder, 4 = open folder
    Set-KReg $shellIcons '3' $IconFolder 'String' 'Default folder icon set'
    Set-KReg $shellIcons '4' $IconFolder 'String' 'Open folder icon set'
} else { Write-Kairos "Folder icon not found - skipping" 'WARN' }

# 1c. Drive icon (applies to C:)
if (Test-Path $IconDrive) {
    $driveIcon = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\C\DefaultIcon'
    if (-not (Test-Path $driveIcon)) { New-Item -Path $driveIcon -Force | Out-Null }
    Set-KReg $driveIcon '(default)' $IconDrive 'String' 'C: drive icon set'
} else { Write-Kairos "Drive icon not found - skipping" 'WARN' }

# ============================================================================
#  2. CURSORS
# ============================================================================
Write-Host ""
Write-Kairos "Applying cursors..." 'STEP'
if (Test-Path $CursorDir) {
    # Look for an install.inf to install the scheme automatically
    $inf = Get-ChildItem -Path $CursorDir -Filter *.inf -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($inf) {
        Write-Kairos "Found cursor installer: $($inf.Name)" 'INFO'
        Write-Kairos "Right-click '$($inf.Name)' > Install, then pick the scheme in" 'INFO'
        Write-Kairos "Settings > Bluetooth & devices > Mouse > Additional mouse settings > Pointers." 'INFO'
        Write-Kairos "(INF install needs user action; cannot be fully scripted safely.)" 'WARN'
    } else {
        # No INF: try to map .cur/.ani by common names directly
        Write-Kairos "No .inf found. Mapping cursors by filename..." 'INFO'
        $curMap = @{
            'Arrow'       = @('*normal*','*arrow*','*pointer*')
            'Help'        = @('*help*')
            'AppStarting' = @('*working*','*appstart*','*busy*')
            'Wait'        = @('*busy*','*wait*','*load*')
            'Crosshair'   = @('*precision*','*cross*')
            'IBeam'       = @('*text*','*beam*')
            'NWPen'       = @('*handwriting*','*pen*')
            'No'          = @('*unavailable*','*no*')
            'SizeNS'      = @('*vert*','*ns*')
            'SizeWE'      = @('*horz*','*we*')
            'SizeNWSE'    = @('*dgn1*','*nwse*')
            'SizeNESW'    = @('*dgn2*','*nesw*')
            'SizeAll'     = @('*move*','*all*')
            'Hand'        = @('*link*','*hand*')
        }
        $curBase = 'HKCU:\Control Panel\Cursors'
        $files = Get-ChildItem -Path $CursorDir -Include *.cur,*.ani -File -Recurse -ErrorAction SilentlyContinue
        foreach ($key in $curMap.Keys) {
            foreach ($pat in $curMap[$key]) {
                $match = $files | Where-Object { $_.Name -like $pat } | Select-Object -First 1
                if ($match) { Set-ItemProperty -Path $curBase -Name $key -Value $match.FullName -ErrorAction SilentlyContinue; break }
            }
        }
        Set-ItemProperty -Path $curBase -Name '(default)' -Value 'Kairos' -ErrorAction SilentlyContinue
        # refresh cursors
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KCur { [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a,uint u,IntPtr p,uint f); }
"@ -ErrorAction SilentlyContinue
        [KCur]::SystemParametersInfo(0x0057,0,[IntPtr]::Zero,0) | Out-Null
        Write-Kairos "Cursors mapped (best-effort by filename)" 'OK'
    }
} else {
    Write-Kairos "Cursor folder not found: $CursorDir" 'WARN'
}

# ============================================================================
#  3. SYSTEM FONT (Inter) - OPTIONAL
# ============================================================================
Write-Host ""
if ($ApplyFont) {
    Write-Kairos "Applying system font ($FontFamilyName)..." 'STEP'
    # install the .ttf files first
    if (Test-Path $FontDir) {
        $shellApp = New-Object -ComObject Shell.Application
        $fontsFolder = $shellApp.Namespace(0x14)
        Get-ChildItem -Path $FontDir -Include *.ttf,*.otf -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
            if (-not (Test-Path "$env:SystemRoot\Fonts\$($_.Name)")) {
                Copy-Item $_.FullName "$env:SystemRoot\Fonts\" -Force -ErrorAction SilentlyContinue
                Set-KReg 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts' "$($_.BaseName) (TrueType)" $_.Name 'String' "Installed font: $($_.Name)"
            }
        }
        # substitute Segoe UI with the new family
        $subs = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontSubstitutes'
        Set-KReg $subs 'Segoe UI' $FontFamilyName 'String' "Segoe UI substituted with $FontFamilyName"
        Write-Kairos "Font applied. A REBOOT is required. To revert: delete the 'Segoe UI' substitute value." 'INFO'
    } else {
        Write-Kairos "Font folder not found: $FontDir - download Inter .ttf first" 'WARN'
    }
} else {
    Write-Kairos "Font swap skipped (ApplyFont = false). Set it to \$true when ready." 'INFO'
}

# ============================================================================
#  4. STARTUP SOUND
# ============================================================================
Write-Host ""
Write-Kairos "Setting startup sound..." 'STEP'
if (Test-Path $StartupWav) {
    # enable startup sound
    Set-KReg 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation' 'DisableStartupSound' 0 'DWord' 'Startup sound enabled'
    # point the WindowsLogon sound to the Kairos wav
    $logonSound = 'HKCU:\AppEvents\Schemes\Apps\.Default\WindowsLogon\.Current'
    if (-not (Test-Path $logonSound)) { New-Item -Path $logonSound -Force | Out-Null }
    Set-KReg $logonSound '(default)' $StartupWav 'String' "Logon sound set to Kairos-Startup.wav"
    Write-Kairos "Startup sound configured." 'OK'
    Write-Kairos "Note: Win11 plays the logon sound at sign-in; reboot to hear it." 'INFO'
} else {
    Write-Kairos "Startup wav not found: $StartupWav - skipping" 'WARN'
}

# ============================================================================
#  5. REFRESH
# ============================================================================
Write-Host ""
Write-Kairos "Refreshing (restart Explorer)..." 'STEP'
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }

Write-Host ""
Write-Host "  -------------------------------------" -ForegroundColor DarkGray
Write-Kairos "Layer 1 materials applied." 'OK'
Write-Kairos "Icons refresh after Explorer restart; font/sound need a reboot." 'INFO'
Write-Kairos "Log: $LogFile" 'INFO'
Write-Host ""
