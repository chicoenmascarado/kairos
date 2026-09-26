using System;
using System.Windows.Media.Imaging;

namespace KairosSpot
{
    public enum ResultKind
    {
        App,
        File,
        Setting,
        Calc,
        Web
    }

    public class SearchResult
    {
        public ResultKind Kind { get; set; }
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Glyph { get; set; } = "\uE8FC"; // Segoe Fluent Icons codepoint
        public string Target { get; set; } = "";       // ruta/url/comando a ejecutar
        public string Arguments { get; set; } = "";
        public int Score { get; set; }                 // para ordenar relevancia

        // Icono real extraido del archivo (apps y archivos). Si es null, se
        // muestra el Glyph como respaldo.
        private bool _iconResolved;
        private BitmapSource? _icon;
        public BitmapSource? Icon
        {
            get
            {
                if (!_iconResolved)
                {
                    _iconResolved = true;
                    if (Kind == ResultKind.App || Kind == ResultKind.File)
                        _icon = IconExtractor.GetIcon(Target);
                }
                return _icon;
            }
        }

        // Para el binding: mostrar glifo solo si NO hay icono real.
        public bool HasIcon => Icon != null;
        public System.Windows.Visibility IconVisibility =>
            HasIcon ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility GlyphVisibility =>
            HasIcon ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }
}

