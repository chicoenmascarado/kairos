using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace KairosDock.Interop;

/// <summary>
/// Pulls a high-resolution icon out of an executable / file association using
/// the Windows shell image lists, with no dependency on System.Drawing (and thus
/// no extra NuGet package or GDI handle churn beyond a single HICON we free
/// immediately).
///
/// Strategy: ask the shell for the file's system icon index, fetch the JUMBO
/// (256px) image list, and convert the resulting HICON straight into a WPF
/// <see cref="BitmapSource"/>. Falls back to the large (32px) icon if jumbo
/// isn't available.
/// </summary>
internal static class IconExtractor
{
    /// <summary>
    /// Returns a crisp icon for <paramref name="path"/>, or <c>null</c> if one
    /// can't be produced.
    /// </summary>
    public static BitmapSource? FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        // Resolve to something the shell can stat. For URIs / non-existent paths
        // we just bail and let the caller draw a placeholder.
        if (!File.Exists(path) && !Directory.Exists(path))
            return null;

        // Store / MSIX apps: the exe has no icon of its own, the logo lives in the package.
        if (PackagedAppIcon.TryResolve(path) is { } logo && FromImageFile(logo) is { } packaged)
            return packaged;

        var shfi = new SHFILEINFO();
        // SHGFI_SYSICONINDEX gives us an index into the system image list.
        IntPtr res = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(shfi),
            SHGFI_SYSICONINDEX);
        if (res == IntPtr.Zero)
            return null;

        // Try jumbo (256px) first, then large (48px) for crispness when scaled.
        foreach (int size in new[] { SHIL_JUMBO, SHIL_EXTRALARGE, SHIL_LARGE })
        {
            BitmapSource? bmp = TryGetFromImageList(size, shfi.iIcon);
            if (bmp != null)
                return bmp;
        }
        return null;
    }

    /// <summary>Loads an icon directly from a .png / .ico / image file.</summary>
    public static BitmapSource? FromImageFile(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // don't lock the file
            bmp.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? TryGetFromImageList(int imageListSize, int iconIndex)
    {
        if (SHGetImageList(imageListSize, ref IID_IImageList, out IImageList list) != 0 ||
            list == null)
            return null;

        IntPtr hIcon = IntPtr.Zero;
        try
        {
            // ILD_TRANSPARENT keeps the alpha channel intact.
            list.GetIcon(iconIndex, ILD_TRANSPARENT, ref hIcon);
            if (hIcon == IntPtr.Zero)
                return null;

            var src = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero)
                DestroyIcon(hIcon);
            Marshal.ReleaseComObject(list);
        }
    }

    // --- shell constants ---------------------------------------------------
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const int SHIL_LARGE = 0x0;
    private const int SHIL_EXTRALARGE = 0x2;
    private const int SHIL_JUMBO = 0x4;
    private const int ILD_TRANSPARENT = 0x1;

    private static Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    // Minimal IImageList — we only need GetIcon.
    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
        // (Remaining vtable entries omitted — we never call past GetIcon.)
    }
}
