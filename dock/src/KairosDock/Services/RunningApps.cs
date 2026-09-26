using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace KairosDock.Services;

/// <summary>
/// Answers "is this app actually running, and where is its window?" by enumerating
/// real **Alt-Tab windows** (visible, titled, top-level, non-tool windows) and
/// mapping each to its owning process's executable path.
///
/// This is deliberately stricter than <c>Process.GetProcessesByName</c>: the shell
/// process <c>explorer.exe</c> is *always* running, but it only counts as "File
/// Explorer is open" when an actual folder window exists — and a folder window is
/// an Alt-Tab window while the taskbar/desktop are not. So this approach gives the
/// correct, macOS-like "the app is showing a window" semantics, and powers both the
/// running-indicator dot and click-to-focus.
/// </summary>
internal static class RunningApps
{
    /// <summary>A running app: its executable path and a window to focus.</summary>
    public readonly record struct Entry(string ExePath, IntPtr Window);

    /// <summary>
    /// Snapshot of every app that currently has at least one Alt-Tab window, keyed
    /// by lower-cased full executable path. The window is the first one found
    /// (good enough to foreground the app).
    /// </summary>
    public static Dictionary<string, Entry> Snapshot()
    {
        var map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        EnumWindows((hwnd, _) =>
        {
            if (!IsAltTabWindow(hwnd))
                return true;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
                return true;

            string? path = AppPathForWindow(hwnd, pid);
            if (string.IsNullOrEmpty(path))
                return true;

            // Keep the first window we see for each executable.
            map.TryAdd(path!, new Entry(path!, hwnd));
            return true;
        }, IntPtr.Zero);

        return map;
    }

    /// <summary>True if any running Alt-Tab window belongs to <paramref name="exePath"/>.</summary>
    public static bool IsRunning(Dictionary<string, Entry> snapshot, string exePath)
        => TryFind(snapshot, exePath, out _);

    /// <summary>
    /// Finds a focusable window for <paramref name="exePath"/>. Matches on the full
    /// path when the configured path is rooted/exists, otherwise falls back to a
    /// file-name match (so a bare "explorer.exe" still matches C:\Windows\explorer.exe).
    /// </summary>
    public static bool TryFind(Dictionary<string, Entry> snapshot, string exePath, out IntPtr window)
    {
        window = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        bool rooted = Path.IsPathRooted(exePath) && File.Exists(exePath);
        if (rooted && snapshot.TryGetValue(Path.GetFullPath(exePath), out var exact))
        {
            window = exact.Window;
            return true;
        }

        // File-name fallback.
        string wanted = Path.GetFileName(exePath);
        if (string.IsNullOrEmpty(wanted))
            return false;

        foreach (var entry in snapshot.Values)
        {
            if (string.Equals(Path.GetFileName(entry.ExePath), wanted, StringComparison.OrdinalIgnoreCase))
            {
                window = entry.Window;
                return true;
            }
        }
        return false;
    }

    /// <summary>Brings an existing window to the foreground, restoring if minimized.</summary>
    public static void Focus(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);

        // SetForegroundWindow is rate/focus-limited; the AttachThreadInput dance is
        // the well-worn workaround to reliably steal foreground from another app.
        uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint target = GetWindowThreadProcessId(hwnd, out _);
        if (fg != target)
            AttachThreadInput(fg, target, true);

        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);

        if (fg != target)
            AttachThreadInput(fg, target, false);
    }

    /// <summary>One background app: a window we can focus, its title and exe.</summary>
    public readonly record struct AppWindow(string Title, string ExePath, IntPtr Window);

    /// <summary>
    /// All current Alt-Tab windows (one per window), for the Control Center's
    /// "background apps" list. Distinct from <see cref="Snapshot"/> which collapses
    /// to one entry per executable.
    /// </summary>
    public static List<AppWindow> GetBackgroundApps()
    {
        var list = new List<AppWindow>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsAltTabWindow(hwnd))
                return true;

            GetWindowThreadProcessId(hwnd, out uint pid);
            string? path = pid == 0 ? null : AppPathForWindow(hwnd, pid);
            if (path == "")
                return true;   // cloaked UWP frame: not a real open window

            int len = GetWindowTextLength(hwnd);
            var sb = new StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);

            list.Add(new AppWindow(sb.ToString(), path ?? "", hwnd));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Politely asks a window to close (posts WM_CLOSE).</summary>
    public static void CloseWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    // -----------------------------------------------------------------------

    /// <summary>The standard "would this show in Alt-Tab?" heuristic.</summary>
    private static bool IsAltTabWindow(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd))
            return false;

        // Must be its own root owner (skip owned dialogs/popups).
        IntPtr root = GetAncestor(hwnd, GA_ROOTOWNER);
        if (root != hwnd)
            return false;

        // Skip tool windows (taskbar, tray, the dock itself, etc.).
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0)
            return false;

        // Must have a title.
        return GetWindowTextLength(hwnd) > 0;
    }

    /// <summary>
    /// The identity path of the app behind <paramref name="hwnd"/>: normally its
    /// executable; for UWP frame windows, the hosted app's package manifest (see
    /// <see cref="HostedUwpApp"/>). Returns "" for cloaked frames, which aren't open
    /// windows, and the frame host itself when the hosted app can't be identified.
    /// </summary>
    private static string? AppPathForWindow(IntPtr hwnd, uint pid)
    {
        string? path = GetProcessPath(pid);
        if (path == null || !HostedUwpApp.IsFrameHost(path))
            return path;
        if (HostedUwpApp.IsCloaked(hwnd))
            return "";
        return HostedUwpApp.Resolve(hwnd, pid) ?? path;
    }

    private static string? GetProcessPath(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero)
            return null;
        try
        {
            var sb = new StringBuilder(1024);
            int cap = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref cap) ? sb.ToString() : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    // --- Win32 -------------------------------------------------------------
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint GA_ROOTOWNER = 3;
    private const int SW_RESTORE = 9;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint WM_CLOSE = 0x0010;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder s, int max);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder buf, ref int size);
}
