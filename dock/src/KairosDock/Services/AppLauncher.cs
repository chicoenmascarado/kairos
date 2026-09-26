using System.Diagnostics;
using System.IO;
using KairosDock.Models;

namespace KairosDock.Services;

/// <summary>
/// Launches a dock item or focuses it if it's already on screen. Everything goes
/// through the shell (<c>UseShellExecute = true</c>) so plain exes, system commands
/// (explorer, cmd…), <c>.lnk</c> shortcuts and UWP apps all open the same way the
/// Start menu would open them.
/// </summary>
public static class AppLauncher
{
    /// <summary>
    /// Click handler: bring the app's existing window forward if we can find one,
    /// otherwise launch a fresh instance.
    /// </summary>
    public static void LaunchOrFocus(DockItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
            return;

        // UWP / Store apps are addressed by AppUserModelID, launched through the
        // shell's AppsFolder. We can't reliably focus these, so just (re)launch —
        // Windows brings an already-running UWP app forward itself.
        if (IsUwp(item.Path, out string aumid))
        {
            LaunchUwp(aumid);
            return;
        }

        // Already running with a visible window? Focus it instead of opening a 2nd
        // copy. (Uses Alt-Tab-window enumeration, so it never accidentally "focuses"
        // the always-running explorer.exe shell — only real app windows.)
        var snapshot = RunningApps.Snapshot();
        if (RunningApps.TryFind(snapshot, item.Path, out IntPtr window))
        {
            RunningApps.Focus(window);
            return;
        }

        Launch(item);
    }

    /// <summary>Force-launch a new instance (no focus check).</summary>
    public static void Launch(DockItem item)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = item.Path,
                Arguments = item.Arguments ?? "",
                UseShellExecute = true, // default verb → handles exe, folder, URL, .lnk
                WorkingDirectory = SafeDirectory(item.Path),
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KairosDock] Launch failed for '{item.Path}': {ex.Message}");
        }
    }

    /// <summary>
    /// Closes the app: politely closes its main window, falling back to killing the
    /// process tree. Used by the right-click "Quit".
    /// </summary>
    public static void Quit(DockItem item)
    {
        try
        {
            var snapshot = RunningApps.Snapshot();
            if (!RunningApps.TryFind(snapshot, item.Path, out IntPtr window) || window == IntPtr.Zero)
                return;

            // Try a graceful close first.
            RunningApps.CloseWindow(window);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KairosDock] Quit failed for '{item.Path}': {ex.Message}");
        }
    }

    private static bool IsUwp(string path, out string aumid)
    {
        // Accept either an explicit "shell:AppsFolder\<AUMID>" or a bare AUMID
        // (contains '!' and no path separators, e.g. Microsoft.WindowsStore_8...!App).
        const string prefix = "shell:AppsFolder\\";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            aumid = path[prefix.Length..];
            return true;
        }
        if (path.Contains('!') && !path.Contains('\\') && !path.Contains('/'))
        {
            aumid = path;
            return true;
        }
        aumid = "";
        return false;
    }

    private static void LaunchUwp(string aumid)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"shell:AppsFolder\\{aumid}",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KairosDock] UWP launch failed for '{aumid}': {ex.Message}");
        }
    }

    private static string SafeDirectory(string path)
    {
        try { return Path.IsPathRooted(path) ? (Path.GetDirectoryName(path) ?? "") : ""; }
        catch { return ""; }
    }
}
