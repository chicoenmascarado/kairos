using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Management.Deployment;

namespace KairosDock.Services;

/// <summary>
/// UWP apps (Netflix, Prime Video, Calculator...) don't own their top-level
/// window: it belongs to <c>ApplicationFrameHost.exe</c>, which hosts the app's
/// CoreWindow inside it. Mapping that window to its process therefore yields the
/// frame host — which the dock blocklists — and the app vanishes from the dock.
///
/// This resolves a frame window to the app it hosts and returns the package's
/// <c>AppxManifest.xml</c> path, which the rest of the dock uses as the app's
/// identity: the same path whether the app is running (CoreWindow attached) or
/// minimized/suspended (CoreWindow detached, identified by its AppUserModelID).
/// </summary>
internal static class HostedUwpApp
{
    public const string ManifestName = "AppxManifest.xml";

    public static bool IsFrameHost(string exePath)
        => Path.GetFileName(exePath).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Frame windows linger cloaked (on other virtual desktops or while an app is
    /// shutting down); Alt-Tab hides those, so the dock does too.
    /// </summary>
    public static bool IsCloaked(IntPtr hwnd)
        => DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>
    /// The manifest path of the UWP app hosted by frame window <paramref name="frame"/>,
    /// or <c>null</c> if it can't be identified.
    /// </summary>
    public static string? Resolve(IntPtr frame, uint framePid)
    {
        try
        {
            uint hostedPid = 0;
            EnumChildWindows(frame, (child, _) =>
            {
                GetWindowThreadProcessId(child, out uint pid);
                if (pid != 0 && pid != framePid)
                {
                    hostedPid = pid;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (hostedPid != 0)
            {
                string? dir = PackageDirOfProcess(hostedPid);
                if (IsStoreDir(dir))
                    return Path.Combine(dir!, ManifestName);
                // System apps (Settings...) keep their exe path, so the dock's
                // blocklist still applies to them exactly as before.
                return ProcessPath(hostedPid);
            }

            // Minimized/suspended: the CoreWindow is detached, identify the app by its AUMID.
            string? familyDir = PackageDirOfFamily(FamilyFromAumid(GetAumid(frame)));
            return IsStoreDir(familyDir) ? Path.Combine(familyDir!, ManifestName) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Store apps that run inside another program's process — Netflix and Prime Video
    /// are packaged web apps rendered by msedge.exe — would otherwise be grouped
    /// under that browser and show its icon. Their windows carry the package's
    /// AppUserModelID, which is the only thing that tells them apart. Returns the
    /// package manifest path when the window belongs to a Store package other than
    /// the one <paramref name="exePath"/> lives in; otherwise <c>null</c>.
    /// </summary>
    public static string? StoreAppForWindow(IntPtr hwnd, string exePath)
    {
        try
        {
            string? aumid = GetAumid(hwnd);
            if (string.IsNullOrEmpty(aumid))
                return null;

            // Web apps installed from Chrome/Edge ("Chrome._crx_<id>", "MSEdge._crx_<id>"):
            // the browser writes a Start-menu shortcut with the same AUMID and the app's icon.
            if (aumid.Contains("._crx_", StringComparison.OrdinalIgnoreCase))
                return WebAppShortcuts.Find(aumid);

            string? family = FamilyFromAumid(aumid);
            if (family == null)
                return null;
            string? dir = PackageDirOfFamily(family);
            if (!IsStoreDir(dir) || exePath.StartsWith(dir! + @"\", StringComparison.OrdinalIgnoreCase))
                return null;   // not a Store app, or the exe is the app itself (WhatsApp)
            return Path.Combine(dir!, ManifestName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a web-app AUMID to the Start-menu shortcut that carries it. Used as the
    /// app's identity path: the shell draws the shortcut's icon, and its file name is
    /// the app name. The scan is cached and redone at most every 30 s when an
    /// unknown AUMID shows up (a web app installed while the dock is running).
    /// </summary>
    private static class WebAppShortcuts
    {
        private static Dictionary<string, string> _byAumid = new(StringComparer.OrdinalIgnoreCase);
        private static DateTime _lastScan = DateTime.MinValue;
        private static readonly object Gate = new();

        public static string? Find(string aumid)
        {
            lock (Gate)
            {
                if (_byAumid.TryGetValue(aumid, out var hit))
                    return hit;
                if (DateTime.UtcNow - _lastScan < TimeSpan.FromSeconds(30))
                    return null;
                _byAumid = Scan();
                _lastScan = DateTime.UtcNow;
                return _byAumid.TryGetValue(aumid, out hit) ? hit : null;
            }
        }

        private static Dictionary<string, string> Scan()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                     })
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    string? id = GetFileAumid(lnk);
                    if (!string.IsNullOrEmpty(id) && id.Contains("._crx_", StringComparison.OrdinalIgnoreCase))
                        map.TryAdd(id, lnk);
                }
            }
            return map;
        }
    }

    /// <summary>True if <paramref name="path"/> is a package manifest used as an app identity.</summary>
    public static bool IsManifestIdentity(string path)
        => path.EndsWith(@"\" + ManifestName, StringComparison.OrdinalIgnoreCase)
           && path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Stable grouping key for a package: its folder name up to the version ("4DF9E0F8.Netflix").</summary>
    public static string PackageKey(string manifestPath)
    {
        string folder = Path.GetFileName(Path.GetDirectoryName(manifestPath)) ?? "";
        int cut = folder.IndexOf('_');
        return cut > 0 ? folder[..cut] : folder;
    }

    private static bool IsStoreDir(string? dir)
        => dir != null && dir.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    private static string? ProcessPath(uint pid)
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
        finally
        {
            CloseHandle(h);
        }
    }

    private static string? PackageDirOfProcess(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero)
            return null;
        try
        {
            uint len = 0;
            if (GetPackageFullName(h, ref len, null) != ERROR_INSUFFICIENT_BUFFER)
                return null;   // not a packaged process
            var name = new StringBuilder((int)len);
            if (GetPackageFullName(h, ref len, name) != 0)
                return null;

            uint pathLen = 0;
            if (GetPackagePathByFullName(name.ToString(), ref pathLen, null) != ERROR_INSUFFICIENT_BUFFER)
                return null;
            var path = new StringBuilder((int)pathLen);
            return GetPackagePathByFullName(name.ToString(), ref pathLen, path) == 0 ? path.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    // Window enumeration runs every 750 ms; package lookups are slow, so cache them.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> FamilyDirs =
        new(StringComparer.OrdinalIgnoreCase);

    private static string? PackageDirOfFamily(string? family)
    {
        if (string.IsNullOrEmpty(family))
            return null;
        return FamilyDirs.GetOrAdd(family, f =>
        {
            // Empty user SID = the current user; needs no elevation.
            var pkg = new PackageManager().FindPackagesForUser(string.Empty, f).FirstOrDefault();
            return pkg?.InstalledLocation?.Path;
        });
    }

    private static string? FamilyFromAumid(string? aumid)
    {
        if (string.IsNullOrEmpty(aumid))
            return null;
        int bang = aumid.IndexOf('!');
        return bang > 0 ? aumid[..bang] : null;
    }

    private static string? GetAumid(IntPtr hwnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out IPropertyStore store) != 0 || store == null)
            return null;
        return ReadAumid(store);
    }

    private static string? GetFileAumid(string path)
    {
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, GPS_DEFAULT, ref iid, out IPropertyStore store) != 0 || store == null)
                return null;
            return ReadAumid(store);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads System.AppUserModel.ID from a property store and releases it.</summary>
    private static string? ReadAumid(IPropertyStore store)
    {
        try
        {
            var key = PKEY_AppUserModel_ID;
            if (store.GetValue(ref key, out PROPVARIANT pv) != 0)
                return null;
            try
            {
                return pv.vt == VT_LPWSTR ? Marshal.PtrToStringUni(pv.pointer) : null;
            }
            finally
            {
                PropVariantClear(ref pv);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    // --- Win32 -------------------------------------------------------------
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int DWMWA_CLOAKED = 14;
    private const ushort VT_LPWSTR = 31;

    private static PROPERTYKEY PKEY_AppUserModel_ID =
        new() { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointer;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, out IPropertyStore store);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindCtx, int flags, ref Guid riid, out IPropertyStore store);
    private const int GPS_DEFAULT = 0;
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PROPVARIANT pv);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder buf, ref int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder? path);
}
