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

    private static string? PackageDirOfFamily(string? family)
    {
        if (string.IsNullOrEmpty(family))
            return null;
        // Empty user SID = the current user; needs no elevation.
        var pkg = new PackageManager().FindPackagesForUser(string.Empty, family).FirstOrDefault();
        return pkg?.InstalledLocation?.Path;
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
