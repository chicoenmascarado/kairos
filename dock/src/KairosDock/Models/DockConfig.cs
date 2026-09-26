using System.Text.Json.Serialization;

namespace KairosDock.Models;

/// <summary>
/// Strongly-typed view of <c>kairos-dock.json</c>. Everything has a sensible
/// default so a sparse / partial config file still produces a usable dock.
/// </summary>
public sealed class DockConfig
{
    /// <summary>The pinned apps shown in the dock, left-to-right.</summary>
    [JsonPropertyName("items")]
    public List<DockItem> Items { get; set; } = new();

    /// <summary>Tuning knobs for the magnification feel and visuals.</summary>
    [JsonPropertyName("appearance")]
    public DockAppearance Appearance { get; set; } = new();

    /// <summary>
    /// Slide the dock off-screen when idle and reveal it when the cursor hits
    /// the bottom edge. Default OFF for v1.
    /// </summary>
    [JsonPropertyName("autoHide")]
    public bool AutoHide { get; set; } = false;

    /// <summary>Start KairosDock automatically with Windows (per-user Run key).</summary>
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; } = false;

    /// <summary>
    /// Hide the Windows taskbar so KairosDock is the shell. Restored when the dock
    /// exits. Default OFF — turn on inside the Kairos VM.
    /// </summary>
    [JsonPropertyName("hideWindowsTaskbar")]
    public bool HideWindowsTaskbar { get; set; } = false;
}

/// <summary>Visual + motion parameters. Tune these to change the "feel".</summary>
public sealed class DockAppearance
{
    /// <summary>Resting (un-magnified) icon edge length, in DIPs.</summary>
    [JsonPropertyName("iconSize")]
    public double IconSize { get; set; } = 52;

    /// <summary>Horizontal gap between icons, in DIPs.</summary>
    [JsonPropertyName("iconSpacing")]
    public double IconSpacing { get; set; } = 16;

    /// <summary>Peak magnification factor for the icon directly under the cursor.</summary>
    [JsonPropertyName("maxScale")]
    public double MaxScale { get; set; } = 1.9;

    /// <summary>
    /// How far the magnification "bump" reaches, in DIPs. Larger = wider,
    /// softer falloff that lifts more neighbours.
    /// </summary>
    [JsonPropertyName("influence")]
    public double Influence { get; set; } = 95;

    /// <summary>Corner radius of the glass panel, in DIPs.</summary>
    [JsonPropertyName("cornerRadius")]
    public double CornerRadius { get; set; } = 24;

    /// <summary>Distance of the dock from the bottom of the work area, in DIPs.</summary>
    [JsonPropertyName("bottomMargin")]
    public double BottomMargin { get; set; } = 0;

    /// <summary>
    /// Real DWM acrylic blur behind the glass. OFF by default: on Windows 10 it
    /// fills a rectangular region (clipped here to the rounded panel) and is less
    /// reliable than the layered translucent panel, which already looks premium.
    /// </summary>
    [JsonPropertyName("blur")]
    public bool Blur { get; set; } = false;

    /// <summary>Show the clock at the right end of the dock.</summary>
    [JsonPropertyName("showClock")]
    public bool ShowClock { get; set; } = true;
}
