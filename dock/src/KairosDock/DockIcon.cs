using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KairosDock.Models;
using KairosDock.Motion;

namespace KairosDock;

/// <summary>
/// Bundles a config item with its live visual and the four springs that drive its
/// motion. Keeping the springs here (one set per icon) is what lets every icon
/// animate independently yet share the one render loop.
/// </summary>
internal sealed class DockIcon
{
    public DockIcon(DockItem item, Border host, ScaleTransform scale,
                    TranslateTransform translate, Ellipse dot)
    {
        Item = item;
        Host = host;
        Scale = scale;
        Translate = translate;
        Dot = dot;

        // Magnification settle — near-critically-damped for a glassy, calm scale.
        ScaleSpring = new Spring(DockTuning.MagStiffness, DockTuning.MagDamping);
        // Launch bounce — under-damped so it hops a couple of times and decays.
        BounceSpring = new Spring(DockTuning.BounceStiffness, DockTuning.BounceDamping);
        // Horizontal position — makes the row flow and reorders glide.
        PosSpring = new Spring(DockTuning.PosStiffness, DockTuning.PosDamping);
        // Entrance pop / removal collapse (scale multiplier 0→1).
        PopSpring = new Spring(DockTuning.PopStiffness, DockTuning.PopDamping);
    }

    public DockItem Item { get; }
    public Border Host { get; }
    public ScaleTransform Scale { get; }
    public TranslateTransform Translate { get; }
    public Ellipse Dot { get; }

    public Spring ScaleSpring { get; }
    public Spring BounceSpring { get; }
    public Spring PosSpring { get; }
    public Spring PopSpring { get; }

    /// <summary>Resting centre X (window coords), anchors the magnification falloff.</summary>
    public double RestCenter { get; set; }

    /// <summary>True while a matching app window exists (drives the running dot).</summary>
    public bool IsRunning { get; set; }

    /// <summary>A window handle for the running app (for the hover preview); else zero.</summary>
    public IntPtr Window { get; set; }

    /// <summary>Entrance stagger: seconds after entrance start before this pops in.</summary>
    public double EntranceDelay { get; set; }

    /// <summary>Set once the entrance delay has elapsed and the pop is animating.</summary>
    public bool Started { get; set; }

    /// <summary>When true the pop spring collapses to 0 and the icon is then removed.</summary>
    public bool Removing { get; set; }

    /// <summary>False until the icon's position spring has been seeded to its slot.</summary>
    public bool PosInitialized { get; set; }

    /// <summary>Composite render scale: magnification × entrance/removal pop.</summary>
    public double CurrentScale => ScaleSpring.Value * System.Math.Max(0, PopSpring.Value);
}
