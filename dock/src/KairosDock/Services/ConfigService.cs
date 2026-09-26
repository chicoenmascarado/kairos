using System.IO;
using System.Text.Json;
using KairosDock.Models;

namespace KairosDock.Services;

/// <summary>Loads and saves <c>kairos-dock.json</c>.</summary>
public static class ConfigService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The on-disk config path, next to the running executable.</summary>
    public static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "kairos-dock.json");

    /// <summary>
    /// Reads the config, falling back to an empty default if the file is missing
    /// or malformed (so the app always starts). Environment variables in item
    /// paths/icons are expanded here.
    /// </summary>
    public static DockConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<DockConfig>(json, Options);
                if (cfg != null)
                {
                    foreach (var item in cfg.Items)
                    {
                        item.Path = Environment.ExpandEnvironmentVariables(item.Path ?? "");
                        if (item.Icon != null)
                            item.Icon = Environment.ExpandEnvironmentVariables(item.Icon);
                    }
                    return cfg;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KairosDock] Failed to load config: {ex}");
        }
        return new DockConfig();
    }

    /// <summary>Persists the current config (used by "Remove from dock").</summary>
    public static void Save(DockConfig config)
    {
        try
        {
            string json = JsonSerializer.Serialize(config, Options);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KairosDock] Failed to save config: {ex}");
        }
    }
}
