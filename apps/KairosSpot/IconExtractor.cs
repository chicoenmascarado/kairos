using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace KairosSpot
{
    // Extrae el icono real de un .lnk / .exe / archivo y lo entrega como
    // ImageSource para WPF. Cachea por ruta para no re-extraer en cada tecla.
    public static class IconExtractor
    {
        private static readonly Dictionary<string, BitmapSource?> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public static BitmapSource? GetIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_cache.TryGetValue(path, out var cached)) return cached;

            BitmapSource? result = null;
            try
            {
                var info = new Interop.SHFILEINFO();
                IntPtr hImg = Interop.SHGetFileInfo(
                    path, 0, ref info,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf(info),
                    Interop.SHGFI_ICON | Interop.SHGFI_LARGEICON);

                if (info.hIcon != IntPtr.Zero)
                {
                    result = Imaging.CreateBitmapSourceFromHIcon(
                        info.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    result?.Freeze(); // permite usarlo en cualquier hilo / binding
                    Interop.DestroyIcon(info.hIcon);
                }
            }
            catch
            {
                result = null;
            }

            _cache[path] = result;
            return result;
        }
    }
}
