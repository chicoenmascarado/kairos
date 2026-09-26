using System;
using System.Windows.Media.Imaging;

namespace KairosMenu
{
    public class AppEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";   // ruta al .lnk o .exe

        private bool _iconResolved;
        private BitmapSource? _icon;
        public BitmapSource? Icon
        {
            get
            {
                if (!_iconResolved)
                {
                    _iconResolved = true;
                    _icon = IconExtractor.GetIcon(Path);
                }
                return _icon;
            }
        }
    }
}
