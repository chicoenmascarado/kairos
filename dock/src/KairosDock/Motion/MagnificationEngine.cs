namespace KairosDock.Motion;

/// <summary>
/// Pure, side-effect-free magnification math. Given where the cursor is and
/// where an icon "lives" at rest, it returns how big that icon wants to be.
///
/// Kept deliberately separate from any WPF / layout code so the curve can be
/// tuned and unit-reasoned in isolation — this is the file to poke at when we
/// iterate on feel.
///
/// The falloff is a Gaussian bump:
///
///     scale(d) = 1 + (maxScale - 1) * exp( -(d^2) / (2 * sigma^2) )
///
/// where <c>d</c> is the horizontal distance (DIPs) from the cursor to the
/// icon's resting centre. A Gaussian is C-infinity smooth — there are no kinks
/// or steps anywhere in the curve, so neighbours lift and settle continuously
/// as the cursor slides, which is exactly the macOS quality we're after.
/// </summary>
public sealed class MagnificationEngine
{
    private double _maxScale;
    private double _sigma;

    /// <param name="maxScale">Scale of the icon directly under the cursor (e.g. 1.9).</param>
    /// <param name="influence">
    /// Reach of the bump in DIPs. Interpreted as ~2 standard deviations, so this
    /// is roughly the distance at which an icon has mostly returned to rest.
    /// </param>
    public MagnificationEngine(double maxScale, double influence)
    {
        Configure(maxScale, influence);
    }

    /// <summary>Update the curve parameters live (e.g. after a config reload).</summary>
    public void Configure(double maxScale, double influence)
    {
        _maxScale = System.Math.Max(1.0, maxScale);
        // Map the friendly "influence" reach onto the Gaussian's sigma. Guard
        // against zero so we never divide by it below.
        _sigma = System.Math.Max(1.0, influence) / 2.0;
    }

    /// <summary>Peak scale, exposed so layout can size things for the worst case.</summary>
    public double MaxScale => _maxScale;

    /// <summary>
    /// The scale an icon resting at <paramref name="iconCenter"/> wants, given the
    /// cursor at <paramref name="cursorX"/>. Returns 1.0 when the cursor is absent
    /// (<paramref name="cursorActive"/> = false) so the dock relaxes flat.
    /// </summary>
    public double ScaleFor(double iconCenter, double cursorX, bool cursorActive)
    {
        if (!cursorActive)
            return 1.0;

        double d = cursorX - iconCenter;
        double exponent = -(d * d) / (2.0 * _sigma * _sigma);
        return 1.0 + (_maxScale - 1.0) * System.Math.Exp(exponent);
    }
}
