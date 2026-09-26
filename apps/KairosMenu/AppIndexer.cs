using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KairosMenu
{
    // Indexa apps desde los .lnk del menu inicio (usuario + maquina).
    // Misma fuente fiable que usa KaiSpot.
    public class AppIndexer
    {
        public List<AppEntry> All { get; } = new();

        public void Build()
        {
            All.Clear();

            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                IEnumerable<string> links;
                try { links = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
                catch { continue; }

                foreach (var lnk in links)
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var lower = name.ToLowerInvariant();
                    if (lower.Contains("uninstall") || lower.Contains("desinstal") ||
                        lower.Contains("readme") || lower.Contains("help"))
                        continue;

                    if (!seen.Add(name)) continue;

                    All.Add(new AppEntry { Name = name, Path = lnk });
                }
            }

            SortByUsageThenName();
        }

        public void SortByUsageThenName()
        {
            All.Sort((a, b) =>
            {
                int ua = UsageTracker.CountFor(a.Path);
                int ub = UsageTracker.CountFor(b.Path);
                if (ua != ub) return ub.CompareTo(ua);          // mas usadas primero
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        // Filtra por texto (prefijo o contiene). Vacio = todas.
        public List<AppEntry> Filter(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return All.ToList();
            var q = query.Trim().ToLowerInvariant();

            return All
                .Where(a => a.Name.ToLowerInvariant().Contains(q))
                .OrderByDescending(a => a.Name.ToLowerInvariant().StartsWith(q))
                .ThenByDescending(a => UsageTracker.CountFor(a.Path))
                .ThenBy(a => a.Name.Length)
                .ToList();
        }
    }
}
