using System.Text.Json.Serialization;

namespace KairosDock.Models;

/// <summary>One pinned application in the dock.</summary>
public sealed class DockItem
{
    /// <summary>Tooltip / display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Path (or shell command) to launch. Environment variables such as
    /// <c>%WINDIR%</c> are expanded at load time.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>Optional launch arguments.</summary>
    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    /// <summary>
    /// Optional explicit icon (.png / .ico). When omitted, the icon is pulled
    /// from the target executable automatically.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>
    /// Whether this item is pinned (stays in the dock even when not running).
    /// Defaults to true — everything listed in the config is pinned unless told
    /// otherwise.
    /// </summary>
    [JsonPropertyName("pinned")]
    public bool Pinned { get; set; } = true;
}
