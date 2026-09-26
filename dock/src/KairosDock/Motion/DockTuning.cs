namespace KairosDock.Motion;

/// <summary>
/// Every knob that controls how the dock *feels*, in one place. These are the
/// numbers to fiddle with when tuning toward "buttery macOS". Values here are the
/// defaults; <c>kairos-dock.json</c> can override the magnification ones per-user.
///
/// Spring intuition:
///   • stiffness  → how hard it's pulled to target (higher = snappier).
///   • damping    → how fast energy bleeds off (higher = less overshoot/bounce).
///   • critical damping = 2·√stiffness. Below it the spring overshoots (lively);
///     at/above it the spring eases in with no overshoot (calm). macOS magnify is
///     ~critically damped; the launch bounce is well below critical (it hops).
/// </summary>
internal static class DockTuning
{
    // --- Magnification curve ----------------------------------------------
    /// <summary>Scale of the icon directly under the cursor.</summary>
    public const double MaxScale = 1.6;

    /// <summary>
    /// Reach of the magnification bump in DIPs (≈2σ of the Gaussian). Bigger =
    /// more neighbours lift, softer/wider wave.
    /// </summary>
    public const double Influence = 90;

    // --- Magnification settle spring (critically-damped-ish: smooth, no bounce) -
    public const double MagStiffness = 240;
    public const double MagDamping = 30;   // 2·√240 ≈ 31 → just-shy-of-critical, glassy settle

    // --- Launch bounce spring (under-damped: a couple of decaying hops) --------
    public const double BounceStiffness = 260;
    public const double BounceDamping = 13;   // ≪ 2·√260 ≈ 32 → visibly hops, then settles
    /// <summary>Upward velocity impulse (DIPs/s) applied on launch. Negative = up.</summary>
    public const double BounceImpulse = -300;

    // --- Dock-wide slide spring (entrance / auto-hide) ------------------------
    public const double SlideStiffness = 150;
    public const double SlideDamping = 19;   // slight overshoot for a graceful arrival

    // --- Per-icon horizontal position spring (the row "flows" + reorder shift) -
    public const double PosStiffness = 260;
    public const double PosDamping = 26;

    // --- Entrance stagger ------------------------------------------------------
    /// <summary>Delay between successive icons popping in, in seconds.</summary>
    public const double EntranceStaggerStep = 0.045;
    /// <summary>Per-icon pop-in spring (scale 0→1).</summary>
    public const double PopStiffness = 300;
    public const double PopDamping = 24;
}
