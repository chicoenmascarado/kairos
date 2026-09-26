using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace KairosDock.Interop;

/// <summary>
/// Makes KairosDock behave like the system shell: optional auto-start with Windows
/// and optional hiding of the real Windows taskbar (restored on exit).
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

    /// <summary>Hides the primary + secondary Windows taskbars.</summary>
    public static void HideTaskbar()
    {
        try
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero) ShowWindow(tray, SW_HIDE);

            // Taskbars on additional monitors.
            IntPtr secondary = IntPtr.Zero;
            while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                ShowWindow(secondary, SW_HIDE);

            _hidden = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KairosDock] hide taskbar failed: {ex.Message}");
        }
    }

    /// <summary>Restores the taskbars (called on exit). Safe to call if never hidden.</summary>
    public static void RestoreTaskbar()
    {
        if (!_hidden)
            return;
        try
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero) ShowWindow(tray, SW_SHOW);

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
