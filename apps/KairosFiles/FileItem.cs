using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace KairosFiles
{
    public class FileItem
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }

        public string TypeLabel => IsDirectory ? "Carpeta" : (Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() + " archivo").Trim();

        public string SizeLabel
        {
            get
            {
                if (IsDirectory) return "";
                return FormatSize(Size);
            }
        }

        public string ModifiedLabel => Modified == DateTime.MinValue ? "" : Modified.ToString("dd/MM/yyyy HH:mm");

        private bool _iconResolved;
        private BitmapSource? _icon;
        public BitmapSource? Icon
        {
            get
            {
                if (!_iconResolved)
                {
                    _iconResolved = true;
                    _icon = IconExtractor.GetIcon(FullPath, IsDirectory);
                }
                return _icon;
            }
        }

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double s = bytes;
            int u = 0;
            while (s >= 1024 && u < units.Length - 1) { s /= 1024; u++; }
            return u == 0 ? $"{bytes} B" : $"{s:0.#} {units[u]}";
        }
    }
}
