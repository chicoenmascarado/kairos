using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KairosMenu
{
    // Cuenta cuantas veces se ha lanzado cada resultado (por su Target) y lo
    // persiste en un archivo simple en LOCALAPPDATA. Sirve para que lo mas
    // usado suba en los resultados.
    public static class UsageTracker
    {
        private static readonly Dictionary<string, int> _counts =
            new(StringComparer.OrdinalIgnoreCase);

        private static string FilePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Kairos", "KaiSpot");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "usage.tsv");
            }
        }

        public static void Load()
        {
            _counts.Clear();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var tab = line.LastIndexOf('\t');
                    if (tab <= 0) continue;
                    var key = line.Substring(0, tab);
                    if (int.TryParse(line.Substring(tab + 1), out var n))
                        _counts[key] = n;
                }
            }
            catch { }
        }

        public static void Record(string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            _counts.TryGetValue(target, out var n);
            _counts[target] = n + 1;
            Save();
        }

        public static int CountFor(string target)
        {
            if (string.IsNullOrEmpty(target)) return 0;
            return _counts.TryGetValue(target, out var n) ? n : 0;
        }

        private static void Save()
        {
            try
            {
                var lines = _counts
                    .OrderByDescending(kv => kv.Value)
                    .Take(500) // no dejar crecer sin limite
                    .Select(kv => $"{kv.Key}\t{kv.Value}");
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }
}
