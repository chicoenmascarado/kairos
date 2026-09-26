using Windows.Devices.Radios;

namespace KairosDock.Services;

/// <summary>
/// Real Wi-Fi / Bluetooth on-off via the WinRT <see cref="Radio"/> API (no admin
/// needed). All calls are best-effort and return null / no-op when unavailable.
/// </summary>
internal static class RadioControl
{
    /// <summary>True/false if the radio exists and we can read it; null otherwise.</summary>
    public static async Task<bool?> IsOnAsync(RadioKind kind)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
                return null;
            foreach (var r in await Radio.GetRadiosAsync())
                if (r.Kind == kind)
                    return r.State == RadioState.On;
            return null;
        }
        catch { return null; }
    }

    /// <summary>Flips the radio on↔off.</summary>
    public static async Task ToggleAsync(RadioKind kind)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
                return;
            foreach (var r in await Radio.GetRadiosAsync())
            {
                if (r.Kind == kind)
                {
                    await r.SetStateAsync(r.State == RadioState.On ? RadioState.Off : RadioState.On);
                    return;
                }
            }
        }
        catch { /* best-effort */ }
    }
}
