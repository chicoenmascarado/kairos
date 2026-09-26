using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace KairosFiles
{
    // Extrae el icono real de un .lnk / .exe / archivo y lo entrega como
    // ImageSource para WPF. Cachea por ruta para no re-extraer en cada tecla.
    public static class IconExtractor
    {
        private static readonly Dictionary<string, BitmapSource?> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public static BitmapSource? GetIcon(string path)
        {
            return GetIcon(path, false);
        }

        public static BitmapSource? GetIcon(string path, bool isDirectory)
        {
            if (string.IsNullOrEmpty(path)) return null;
            // Clave de cache: carpetas comparten icono; archivos se cachean por
            // extension (todos los .pdf comparten icono, etc.). Ejecutables y
            // accesos directos se cachean por ruta (icono propio de cada uno).
            string cacheKey;
            if (isDirectory) cacheKey = "DIR";
            else
            {
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                cacheKey = (ext == ".exe" || ext == ".lnk" || ext == ".ico" || ext == "")
                    ? path : "EXT:" + ext;
            }
            if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

            BitmapSource? result = null;
            try
            {
                var info = new Interop.SHFILEINFO();
                uint flags = Interop.SHGFI_ICON | Interop.SHGFI_LARGEICON;
                uint attrs = 0;

                if (isDirectory)
                {
                    // Icono generico de carpeta sin tocar el disco.
                    flags |= Interop.SHGFI_USEFILEATTRIBUTES;
                    attrs = 0x10; // FILE_ATTRIBUTE_DIRECTORY
                }

                IntPtr hImg = Interop.SHGetFileInfo(
                    path, attrs, ref info,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf(info),
                    flags);

                if (info.hIcon != IntPtr.Zero)
                {
                    result = Imaging.CreateBitmapSourceFromHIcon(
                        info.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    result?.Freeze();
                    Interop.DestroyIcon(info.hIcon);
                }
            }
            catch
            {
                result = null;
            }

            _cache[cacheKey] = result;
            return result;
        }
    }
}
