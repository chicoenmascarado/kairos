using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KairosSpot
{
    public class SearchEngine
    {
        private readonly AppIndexer _apps = new();
        private List<SearchResult> _recentFiles = new();

        public void Initialize()
        {
            UsageTracker.Load();
            _apps.Build();
            LoadRecentFiles();
        }

        // Lee la carpeta Recent de Windows (.lnk a archivos abiertos recientemente).
        private void LoadRecentFiles()
        {
            _recentFiles = new List<SearchResult>();
            var recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (string.IsNullOrEmpty(recent) || !Directory.Exists(recent)) return;

            try
            {
                var files = new DirectoryInfo(recent)
                    .GetFiles("*.lnk")
                    .OrderByDescending(f => f.LastAccessTimeUtc)
                    .Take(60);

                foreach (var f in files)
                {
                    var name = Path.GetFileNameWithoutExtension(f.Name);
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Filtrar ruido: comandos de sistema, capturas, urls internas, etc.
                    var lower = name.ToLowerInvariant();
                    if (lower.StartsWith("ms-") ||           // ms-screenclip---source=HotKey
                        lower.Contains("---source=") ||
                        lower.Contains("://") ||
                        lower.Length < 2)
                        continue;

                    _recentFiles.Add(new SearchResult
                    {
                        Kind = ResultKind.File,
                        Title = name,
                        Subtitle = "Reciente",
                        Glyph = GlyphForFile(name),
                        Target = f.FullName
                    });
                }
            }
            catch { }
        }

        // Busqueda superficial en las carpetas tipicas del usuario. Limitada en
        // profundidad y cantidad para que sea instantanea (no es un indexador).
        private IEnumerable<SearchResult> SearchUserFolders(string ql)
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };

            var found = new List<SearchResult>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int budget = 200; // tope de archivos a inspeccionar

            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                if (budget <= 0) break;

                IEnumerable<string> files;
                try
                {
                    var opts = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        MaxRecursionDepth = 2,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
                    };
                    files = Directory.EnumerateFiles(root, "*", opts);
                }
                catch { continue; }

                foreach (var path in files)
                {
                    if (budget-- <= 0) break;
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!name.ToLowerInvariant().Contains(ql)) continue;
                    if (!seen.Add(path)) continue;

                    var full = Path.GetFileName(path);
                    found.Add(new SearchResult
                    {
                        Kind = ResultKind.File,
                        Title = full,
                        Subtitle = "Archivo",
                        Glyph = GlyphForFile(full),
                        Target = path,
                        Score = MatchScore(name.ToLowerInvariant(), ql) - 20
                    });

                    if (found.Count >= 12) return found;
                }
            }
            return found;
        }

        // Glifo (Segoe Fluent Icons) segun la extension del archivo.
        private static string GlyphForFile(string name)
        {
            var ext = Path.GetExtension(name).ToLowerInvariant();
            return ext switch
            {
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "\uEB9F",
                ".mp3" or ".wav" or ".flac" or ".m4a" => "\uE8D6",
                ".mp4" or ".mkv" or ".avi" or ".mov" => "\uE714",
                ".pdf" => "\uEA90",
                ".doc" or ".docx" => "\uE8A5",
                ".xls" or ".xlsx" or ".csv" => "\uE8A5",
                ".zip" or ".rar" or ".7z" => "\uF012",
                ".txt" or ".log" or ".md" => "\uE8A5",
                ".ps1" or ".bat" or ".cmd" => "\uE756",
                _ => "\uE7C3"
            };
        }

        public List<SearchResult> Query(string raw)
        {
            var q = (raw ?? "").Trim();
            var results = new List<SearchResult>();

            if (q.Length == 0)
            {
                // Estado vacio: mostrar lo mas usado primero, luego recientes.
                var suggestions = _recentFiles
                    .OrderByDescending(f => UsageTracker.CountFor(f.Target))
                    .Take(6);
                results.AddRange(suggestions);
                return results;
            }

            // 1) Calculo directo
            if (Calculator.TryEval(q, out var calc))
            {
                results.Add(new SearchResult
                {
                    Kind = ResultKind.Calc,
                    Title = FormatNumber(calc),
                    Subtitle = $"{q} =",
                    Glyph = "\uE8EF",
                    Target = FormatNumber(calc)
                });
            }

            var ql = q.ToLowerInvariant();

            // 2) Apps (match por prefijo y por contiene)
            foreach (var app in _apps.Apps)
            {
                int s = MatchScore(app.Title.ToLowerInvariant(), ql);
                if (s > 0)
                {
                    // Bonus por frecuencia de uso (cap a +40 para no dominar el match).
                    app.Score = s + Math.Min(UsageTracker.CountFor(app.Target) * 4, 40);
                    results.Add(app);
                }
            }

            // 3) Ajustes del sistema
            foreach (var setting in SettingsCatalog.Items)
            {
                int best = MatchScore(setting.Title.ToLowerInvariant(), ql);
                foreach (var kw in setting.Keywords)
                    best = Math.Max(best, MatchScore(kw, ql));

                if (best > 0)
                {
                    results.Add(new SearchResult
                    {
                        Kind = ResultKind.Setting,
                        Title = setting.Title,
                        Subtitle = "Ajustes del sistema",
                        Glyph = setting.Glyph,
                        Target = setting.Target,
                        Score = best - 5 // ligeramente por debajo de apps con igual match
                    });
                }
            }

            // 4) Archivos recientes que coincidan
            foreach (var f in _recentFiles)
            {
                int s = MatchScore(f.Title.ToLowerInvariant(), ql);
                if (s > 0)
                {
                    f.Score = s - 10;
                    results.Add(f);
                }
            }

            // 4b) Busqueda en carpetas del usuario (solo con 3+ caracteres,
            //     para no ralentizar al teclear las primeras letras).
            if (ql.Length >= 3)
            {
                foreach (var f in SearchUserFolders(ql))
                    results.Add(f);
            }

            // Ordenar por relevancia, calc siempre primero
            var ordered = results
                .OrderByDescending(r => r.Kind == ResultKind.Calc ? int.MaxValue : r.Score)
                .ThenBy(r => r.Title.Length)
                .Take(8)
                .ToList();

            // 5) Siempre ofrecer busqueda web al final
            ordered.Add(new SearchResult
            {
                Kind = ResultKind.Web,
                Title = $"Buscar \"{q}\" en la web",
                Subtitle = "Web",
                Glyph = "\uE774",
                Target = "https://www.google.com/search?q=" + Uri.EscapeDataString(q)
            });

            return ordered;
        }

        // Scoring: prefijo exacto = alto; palabra empieza por = medio; contiene = bajo.
        private static int MatchScore(string text, string query)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return 0;
            if (text == query) return 100;
            if (text.StartsWith(query)) return 80;

            var words = text.Split(' ', '-', '_');
            foreach (var w in words)
                if (w.StartsWith(query)) return 60;

            if (text.Contains(query)) return 30;
            return 0;
        }

        private static string FormatNumber(double d)
        {
            if (Math.Abs(d - Math.Round(d)) < 1e-9)
                return ((long)Math.Round(d)).ToString();
            return Math.Round(d, 6).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
