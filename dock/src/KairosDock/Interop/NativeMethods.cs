using System.Runtime.InteropServices;

namespace KairosDock.Interop;

/// <summary>Small Win32 surface used by the dock window itself.</summary>
internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // --- Start menu (simulate the Windows key) -----------------------------
    private const byte VK_LWIN = 0x5B;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>Opens the Windows Start menu by tapping the Win key.</summary>
    public static void OpenStartMenu()
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>True when the desktop (wallpaper) has focus: the shell reports it as a
    /// "full-screen app", which must not make the dock step aside.</summary>
    public static bool IsDesktopForeground()
    {
        var cls = new System.Text.StringBuilder(64);
        GetClassName(GetForegroundWindow(), cls, cls.Capacity);
        return cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);
}
