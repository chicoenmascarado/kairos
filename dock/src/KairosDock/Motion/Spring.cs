namespace KairosDock.Motion;

/// <summary>
/// A tiny one-dimensional damped spring, integrated with semi-implicit Euler.
///
/// This is what gives the dock its "physical" feel: instead of snapping a value
/// to its target, we accelerate toward the target with a stiffness force and
/// bleed off energy with a damping force. A slightly under-damped spring gives
/// the gentle, lively settle (a whisper of overshoot) that reads as macOS-y.
///
/// The integrator is sub-stepped and dt-clamped so a stutter in the render loop
/// (GC pause, window drag, etc.) can never make the spring explode — that
/// robustness is what keeps the motion jank-free.
/// </summary>
public sealed class Spring
{
    private readonly double _stiffness;   // pull toward target (higher = snappier)
    private readonly double _damping;     // energy loss (higher = less bounce)
    private readonly double _restEpsilon; // below this, snap to target & sleep

    public Spring(double stiffness, double damping, double restEpsilon = 0.0005)
    {
        _stiffness = stiffness;
        _damping = damping;
        _restEpsilon = restEpsilon;
    }

    /// <summary>Current animated value.</summary>
    public double Value { get; private set; }

    /// <summary>Current velocity (units per second).</summary>
    public double Velocity { get; private set; }

    /// <summary>True when the spring is essentially at rest at its target.</summary>
    public bool AtRest { get; private set; } = true;

    /// <summary>Jump immediately to <paramref name="value"/> with no motion.</summary>
    public void Reset(double value)
    {
        Value = value;
        Velocity = 0;
        AtRest = true;
    }

    /// <summary>Add an instantaneous velocity impulse (used for the launch bounce).</summary>
    public void Nudge(double velocity)
    {
        Velocity += velocity;
        AtRest = false;
    }

    /// <summary>
    /// Advance the spring toward <paramref name="target"/> by <paramref name="dt"/>
    /// seconds. dt is clamped and the step is sub-divided so large frame gaps stay
    /// stable. Returns the new <see cref="Value"/>.
    /// </summary>
    public double Step(double target, double dt)
    {
        // A spring that's already parked on its target costs nothing to skip.
        if (AtRest && System.Math.Abs(Value - target) < _restEpsilon)
        {
            Value = target;
            return Value;
        }

        AtRest = false;

        // Clamp the frame time so a hitch can't inject huge energy, then split
        // into fixed-size sub-steps for a stable, frame-rate-independent result.
        dt = System.Math.Min(dt, 0.05);
        const double maxSubStep = 1.0 / 120.0;
        int steps = System.Math.Max(1, (int)System.Math.Ceiling(dt / maxSubStep));
        double h = dt / steps;

        for (int i = 0; i < steps; i++)
        {
            double force = (-_stiffness * (Value - target)) - (_damping * Velocity);
            Velocity += force * h;          // semi-implicit: update velocity first,
            Value += Velocity * h;          // then position using the new velocity.
        }

        // Park the spring once it's close enough and slow enough to matter.
        if (System.Math.Abs(Value - target) < _restEpsilon &&
            System.Math.Abs(Velocity) < _restEpsilon)
        {
            Value = target;
            Velocity = 0;
            AtRest = true;
        }

        return Value;
    }
}
