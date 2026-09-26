using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KairosDock.Interop;

/// <summary>
/// Finds the real icon of a packaged (Microsoft Store / MSIX) app.
///
/// Packaged executables usually carry no icon resource — the shell draws their
/// logo from PNG assets declared in <c>AppxManifest.xml</c>. Asking the shell for
/// the .exe's icon therefore returns the generic "window" icon (WhatsApp, for
/// instance). This reads the manifest next to the exe and picks the largest
/// plated-free variant of the declared logo.
/// </summary>
internal static class PackagedAppIcon
{
    private static readonly Regex TargetSize = new(@"targetsize-(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex Scale = new(@"scale-(\d+)", RegexOptions.IgnoreCase);

    /// <summary>
    /// Returns the path of the best logo PNG for the packaged app that owns
    /// <paramref name="exePath"/>, or <c>null</c> if it isn't a packaged app.
    /// </summary>
    public static string? TryResolve(string exePath)
    {
        try
        {
            if (!exePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
                return null;

            string? packageDir = FindPackageRoot(Path.GetDirectoryName(exePath));
            if (packageDir == null)
                return null;

            XDocument manifest = XDocument.Load(Path.Combine(packageDir, "AppxManifest.xml"));

            // Square44x44Logo ships targetsize variants up to 256px; the others are fallbacks.
            var visual = manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
            var candidates = new List<(string Logo, int BaseSize)>();
            if (visual?.Attribute("Square44x44Logo")?.Value is { Length: > 0 } s44)
                candidates.Add((s44, 44));
            if (visual?.Attribute("Square150x150Logo")?.Value is { Length: > 0 } s150)
                candidates.Add((s150, 150));
            var storeLogo = manifest.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "Logo" && e.Parent?.Name.LocalName == "Properties")?.Value;
            if (!string.IsNullOrWhiteSpace(storeLogo))
                candidates.Add((storeLogo!, 50));

            foreach (var (logo, baseSize) in candidates)
            {
                string? file = BestVariant(packageDir, logo, baseSize);
                if (file != null)
                    return file;
            }
        }
        catch
        {
            // Unreadable manifest or assets: the caller falls back to the shell icon.
        }
        return null;
    }

    /// <summary>
    /// A readable name for the package owning <paramref name="path"/>: the manifest's
    /// DisplayName when it's plain text, else the package name without the publisher
    /// prefix ("4DF9E0F8.Netflix" becomes "Netflix").
    /// </summary>
    public static string DisplayName(string path)
    {
        string? root = FindPackageRoot(Path.GetDirectoryName(path));
        if (root == null)
            return Path.GetFileNameWithoutExtension(path);
        try
        {
            var manifest = XDocument.Load(Path.Combine(root, "AppxManifest.xml"));
            string? name = manifest.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "DisplayName" && e.Parent?.Name.LocalName == "Properties")?.Value;
            if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                return name.Trim();
        }
        catch { /* fall back to the folder name */ }

        string folder = Path.GetFileName(root);
        int cut = folder.IndexOf('_');
        string package = cut > 0 ? folder[..cut] : folder;
        int dot = package.IndexOf('.');
        return dot >= 0 && dot < package.Length - 1 ? package[(dot + 1)..] : package;
    }

    /// <summary>Walks up from the exe folder to the folder holding AppxManifest.xml.</summary>
    private static string? FindPackageRoot(string? dir)
    {
        for (int depth = 0; dir != null && depth < 6; depth++)
        {
            if (File.Exists(Path.Combine(dir, "AppxManifest.xml")))
                return dir;
            if (Path.GetFileName(dir).Equals("WindowsApps", StringComparison.OrdinalIgnoreCase))
                return null;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>
    /// The manifest names a logical file ("Assets\AppList.png"); on disk it exists as
    /// qualified variants ("AppList.targetsize-256_altform-unplated.png",
    /// "AppList.scale-200.png"...). Picks the biggest one, preferring unplated art
    /// (no coloured background tile) and skipping high-contrast assets.
    /// </summary>
    private static string? BestVariant(string packageDir, string logicalPath, int baseSize)
    {
        string full = Path.Combine(packageDir, logicalPath);
        string? folder = Path.GetDirectoryName(full);
        if (folder == null || !Directory.Exists(folder))
            return null;

        string stem = Path.GetFileNameWithoutExtension(full);
        string ext = Path.GetExtension(full);

        string? best = null;
        int bestScore = -1;
        foreach (string file in Directory.EnumerateFiles(folder, stem + "*" + ext))
        {
            string name = Path.GetFileName(file);
            if (name.Contains("contrast-", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("lightunplated", StringComparison.OrdinalIgnoreCase))
                continue;

            int size = baseSize;
            var t = TargetSize.Match(name);
            var s = Scale.Match(name);
            if (t.Success)
                size = int.Parse(t.Groups[1].Value);
            else if (s.Success)
                size = baseSize * int.Parse(s.Groups[1].Value) / 100;

            // Size dominates; unplated wins ties against the plated variant.
            int score = Math.Min(size, 256) * 2 +
                        (name.Contains("altform-unplated", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            if (score > bestScore)
            {
                bestScore = score;
                best = file;
            }
        }
        return best ?? (File.Exists(full) ? full : null);
    }
}
