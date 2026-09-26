using System;
using System.Collections.Generic;
using System.IO;

namespace KairosSpot
{
    // Indexa las apps leyendo los .lnk del Start Menu (usuario + maquina).
    // Es la forma estandar y fiable que usa el propio Windows: barata, sin
    // escanear todo el disco, y captura casi todo lo instalado.
    public class AppIndexer
    {
        public List<SearchResult> Apps { get; } = new();

        public void Build()
        {
            Apps.Clear();

            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    continue;

                IEnumerable<string> links;
                try
                {
                    links = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (var lnk in links)
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Filtrar ruido tipico (desinstaladores, ayudas, etc.)
                    var lower = name.ToLowerInvariant();
                    if (lower.Contains("uninstall") || lower.Contains("desinstal") ||
                        lower.Contains("readme") || lower.Contains("help") ||
                        lower.Contains("(es)") && lower.Contains("ayuda"))
                        continue;

                    if (!seen.Add(name)) continue;

                    Apps.Add(new SearchResult
                    {
                        Kind = ResultKind.App,
                        Title = name,
                        Subtitle = "Aplicacion",
                        Glyph = "\uE71D",
                        Target = lnk
                    });
                }
            }
        }
    }
}
