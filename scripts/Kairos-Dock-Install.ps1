<#
.SYNOPSIS
    Kairos Dock Installer - sets up KairosDock as the system dock in the VM.

.DESCRIPTION
    - Copies KairosDock to C:\Kairos\Dock
    - Hides the Windows taskbar (auto-hide + keep it out of the way)
    - Registers KairosDock to start automatically at logon
    - Starts the dock now

    Run this INSIDE the Kairos VM, after copying the published KairosDock
    folder to the Desktop (or edit $Source below).

    REVERSIBLE: re-enabling the taskbar and removing the startup entry undoes it.

.NOTES
    Project : Kairos
    Phase   : 2 - Interface (dock deployment)
    Run as  : Administrator
#>

#Requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'

# ====== CONFIG ==============================================================
# Folder that contains KairosDock.exe (the published self-contained build).
# Default: a folder named "KairosDock" on the Desktop.
$Source = "$env:USERPROFILE\Desktop\KairosDock"
$Dest   = "C:\Kairos\Dock"
$ExeName = "KairosDock.exe"
# ============================================================================

$LogDir  = "$env:ProgramData\Kairos\logs"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$LogFile = Join-Path $LogDir ("dock_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))

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
Write-Host "    K A I R O S   -   DOCK INSTALLER" -ForegroundColor Magenta
Write-Host "  =====================================" -ForegroundColor Magenta
Write-Host ""
Write-Kairos "This will hide the Windows taskbar and set KairosDock as the dock." 'WARN'
Write-Kairos "VM ONLY. Make sure you have a snapshot." 'WARN'
Write-Host ""
$confirm = Read-Host "  Type 'KAIROS' to continue"
if ($confirm -ne 'KAIROS') { Write-Kairos "Aborted." 'INFO'; return }

# ============================================================================
#  1. COPY THE DOCK
# ============================================================================
Write-Host ""
Write-Kairos "Copying KairosDock..." 'STEP'
if (-not (Test-Path $Source)) {
    Write-Kairos "Source not found: $Source" 'ERROR'
    Write-Kairos "Copy the published KairosDock folder to the Desktop first," 'INFO'
    Write-Kairos "or edit the \$Source path at the top of this script." 'INFO'
    return
}
if (-not (Test-Path "$Source\$ExeName")) {
    Write-Kairos "$ExeName not found inside $Source" 'ERROR'
    Write-Kairos "Make sure the published .exe is in that folder." 'INFO'
    return
}
New-Item -ItemType Directory -Path $Dest -Force | Out-Null
Copy-Item -Path "$Source\*" -Destination $Dest -Recurse -Force
Write-Kairos "Dock copied to $Dest" 'OK'

# ============================================================================
#  2. HIDE THE WINDOWS TASKBAR (fully - auto-hide + push off-screen)
# ============================================================================
Write-Host ""
Write-Kairos "Hiding Windows taskbar completely..." 'STEP'
# Step 2a: enable auto-hide via StuckRects3 (byte 8 = 0x03)
$srPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3'
try {
    $settings = (Get-ItemProperty -Path $srPath -Name Settings -ErrorAction Stop).Settings
    $settings[8] = 3
    Set-ItemProperty -Path $srPath -Name Settings -Value $settings -ErrorAction Stop
    Write-Kairos "Auto-hide enabled" 'OK'
} catch { Write-Kairos "Auto-hide registry step failed ($($_.Exception.Message))" 'WARN' }

# Step 2b: force the taskbar window itself to hidden so it never reveals.
# We find the Shell_TrayWnd window and hide it via Win32. This keeps the tray
# services (clock, network, sound) alive in the background while the bar is
# visually gone. It re-hides on each logon via the startup task below.
$hideCode = @"
using System;
using System.Runtime.InteropServices;
public class KTaskbar {
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string cl, string w);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    public static void Hide() {
        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero) ShowWindow(tray, 0); // SW_HIDE
        IntPtr second = FindWindow("Shell_SecondaryTrayWnd", null);
        if (second != IntPtr.Zero) ShowWindow(second, 0);
    }
    public static void Show() {
        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero) ShowWindow(tray, 5); // SW_SHOW
    }
}
"@
try {
    Add-Type -TypeDefinition $hideCode -ErrorAction Stop
    [KTaskbar]::Hide()
    Write-Kairos "Taskbar window hidden" 'OK'
} catch { Write-Kairos "Could not hide taskbar window ($($_.Exception.Message))" 'WARN' }

# Step 2c: create a tiny startup helper that re-hides the taskbar at each logon
# (Explorer recreates the bar on login, so we re-hide it after the dock starts).
$helperDir = "C:\Kairos\Dock"
New-Item -ItemType Directory -Path $helperDir -Force | Out-Null
$helperPath = "$helperDir\HideTaskbar.ps1"
@'
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class KTaskbar {
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    public static void Hide() {
        IntPtr t = FindWindow("Shell_TrayWnd", null);
        if (t != IntPtr.Zero) ShowWindow(t, 0);
    }
}
"@
Start-Sleep -Seconds 4
[KTaskbar]::Hide()
'@ | Out-File -FilePath $helperPath -Encoding utf8 -Force
Write-Kairos "Created taskbar-hide helper for logon" 'OK'

# ============================================================================
#  3. AUTOSTART KAIROSDOCK + TASKBAR-HIDE AT LOGON
# ============================================================================
Write-Host ""
Write-Kairos "Registering autostart at logon..." 'STEP'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runKey -Name 'KairosDock' -Value "`"$Dest\$ExeName`"" -ErrorAction SilentlyContinue
Write-Kairos "KairosDock set to start at logon" 'OK'
# re-hide taskbar at each logon (Explorer recreates it)
Set-ItemProperty -Path $runKey -Name 'KairosHideTaskbar' `
    -Value "powershell -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$helperPath`"" -ErrorAction SilentlyContinue
Write-Kairos "Taskbar-hide helper set to run at logon" 'OK'

# ============================================================================
#  4. RESTART EXPLORER + LAUNCH DOCK
# ============================================================================
Write-Host ""
Write-Kairos "Applying (restart Explorer + launch dock)..." 'STEP'
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }
Start-Sleep -Seconds 2
Start-Process "$Dest\$ExeName" -ErrorAction SilentlyContinue
Write-Kairos "KairosDock launched." 'OK'

Write-Host ""
Write-Host "  -------------------------------------" -ForegroundColor DarkGray
Write-Kairos "Dock installed. Windows taskbar hidden (auto-hide)." 'OK'
Write-Kairos "The Kairos dock should be visible at the bottom." 'INFO'
Write-Kairos "To undo: set StuckRects3 byte 8 back to 2, remove the Run entry." 'INFO'
Write-Host ""
