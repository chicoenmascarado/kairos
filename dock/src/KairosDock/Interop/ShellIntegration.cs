using System.Diagnostics;
using System.Runtime.InteropServices;
using KairosDock.Services;
using Microsoft.Win32;

namespace KairosDock.Interop;

/// <summary>
/// Makes KairosDock behave like the system shell: optional auto-start with Windows,
/// optional hiding of the real Windows taskbar (restored on exit), and reserving
/// the dock's strip of the screen so maximized apps stop right above it.
/// </summary>
internal static class ShellIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KairosDock";

    /// <summary>Registers or removes KairosDock from the per-user startup Run key.</summary>
    public static void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null)
                return;

            if (enabled)
            {
                string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KairosDock] auto-start failed: {ex.Message}");
        }
    }

    // --- Taskbar hide / restore -------------------------------------------
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private static bool _hidden;
    private static IntPtr _previousAutoHideState = IntPtr.Zero;
    private static System.Windows.Threading.DispatcherTimer? _rehideTimer;

    /// <summary>
    /// Hides the primary + secondary Windows taskbars. The taskbar is also switched
    /// to auto-hide so it stops reserving its strip of the screen (a hidden taskbar
    /// otherwise leaves a dead gap under maximized windows). Windows 11 likes to
    /// bring the taskbar back, so it's re-hidden every couple of seconds.
    /// </summary>
    public static void HideTaskbar()
    {
        try
        {
            IntPtr tray = TrayHost.FindExplorerTray();
            if (tray != IntPtr.Zero)
            {
                var abd = NewAppBarData(tray);
                if (!_hidden)
                    _previousAutoHideState = SHAppBarMessage(ABM_GETSTATE, ref abd);
                abd.lParam = (IntPtr)ABS_AUTOHIDE;
                SHAppBarMessage(ABM_SETSTATE, ref abd);
            }
            HideAll();
            _hidden = true;

            if (_rehideTimer == null)
            {
                _rehideTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _rehideTimer.Tick += (_, _) => HideAll();
                _rehideTimer.Start();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KairosDock] hide taskbar failed: {ex.Message}");
        }
    }

    private static void HideAll()
    {
        IntPtr tray = TrayHost.FindExplorerTray();
        if (tray != IntPtr.Zero && IsWindowVisible(tray))
            ShowWindow(tray, SW_HIDE);

        IntPtr secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (IsWindowVisible(secondary))
                ShowWindow(secondary, SW_HIDE);
        }
    }

    /// <summary>Restores the taskbars (called on exit). Safe to call if never hidden.</summary>
    public static void RestoreTaskbar()
    {
        if (!_hidden)
            return;
        try
        {
            _rehideTimer?.Stop();
            IntPtr tray = TrayHost.FindExplorerTray();
            if (tray != IntPtr.Zero)
            {
                var abd = NewAppBarData(tray);
                abd.lParam = _previousAutoHideState;
                SHAppBarMessage(ABM_SETSTATE, ref abd);
                ShowWindow(tray, SW_SHOW);
            }

            IntPtr secondary = IntPtr.Zero;
            while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                ShowWindow(secondary, SW_SHOW);

            _hidden = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KairosDock] restore taskbar failed: {ex.Message}");
        }
    }

    // --- The dock as an appbar ----------------------------------------------
    private static IntPtr _dockHwnd;
    private static int _reservePx;

    /// <summary>Message the shell sends the dock (position changes, full-screen apps).</summary>
    public static readonly uint AppBarCallbackMessage = RegisterWindowMessage("KairosDockAppBar");

    /// <summary>
    /// Reserves <paramref name="heightPx"/> physical pixels at the bottom of the
    /// primary screen, like the Windows taskbar does, so maximized apps (a DAW, an
    /// editor…) end right above the dock instead of under it.
    /// </summary>
    public static void ReserveBottomStrip(IntPtr dockHwnd, int heightPx)
    {
        try
        {
            var abd = NewAppBarData(dockHwnd);
            if (_dockHwnd == IntPtr.Zero)
            {
                abd.uCallbackMessage = AppBarCallbackMessage;
                SHAppBarMessage(ABM_NEW, ref abd);
            }
            _dockHwnd = dockHwnd;
            _reservePx = heightPx;
            ApplyReservation();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KairosDock] appbar failed: {ex.Message}");
        }
    }

    /// <summary>Re-asserts the reserved strip (after a display change, say).</summary>
    public static void ApplyReservation()
    {
        if (_dockHwnd == IntPtr.Zero)
            return;
        int w = GetSystemMetrics(SM_CXSCREEN), h = GetSystemMetrics(SM_CYSCREEN);
        var abd = NewAppBarData(_dockHwnd);
        abd.uEdge = ABE_BOTTOM;
        abd.rc = new RECT { left = 0, right = w, top = h - _reservePx, bottom = h };
        SHAppBarMessage(ABM_QUERYPOS, ref abd);
        abd.rc.top = abd.rc.bottom - _reservePx;
        SHAppBarMessage(ABM_SETPOS, ref abd);
    }

    public static void ReleaseBottomStrip()
    {
        if (_dockHwnd == IntPtr.Zero)
            return;
        var abd = NewAppBarData(_dockHwnd);
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _dockHwnd = IntPtr.Zero;
    }

    /// <summary>Appbar notification codes (wParam of <see cref="AppBarCallbackMessage"/>).</summary>
    public const int ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;

    // --- Win32 --------------------------------------------------------------

    private static APPBARDATA NewAppBarData(IntPtr hwnd) =>
        new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    private const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    private const uint ABE_BOTTOM = 3;
    private const int ABS_AUTOHIDE = 1;
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;

    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
}
