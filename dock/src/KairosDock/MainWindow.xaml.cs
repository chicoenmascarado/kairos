using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using KairosDock.Interop;
using KairosDock.Models;
using KairosDock.Motion;
using KairosDock.Services;

namespace KairosDock;

/// <summary>
/// The dock window. Owns the single 60fps animation loop that turns cursor position
/// into smooth, spring-settled magnification, plus layout, launching, the running
/// indicator, drag-to-reorder, the clock, and entrance / auto-hide motion.
/// </summary>
public partial class MainWindow : Window
{
    // ---- fixed layout constants (DIPs) ------------------------------------
    private const double PanelPadX = 14;   // horizontal padding inside the glass
    private const double PanelPadY = 9;    // padding above/below the icon row
    private const double DotRoom = 13;     // space below icons for the running dot
    private const double ShadowMargin = 28;// breathing room for shadow + overflow
    private const double DotSize = 5;
    private const double ClockWidth = 54;  // clock cell width
    private const double ClockGap = 16;    // gap between last icon and the clock
    private const double DragThreshold = 6;// px of movement before a click becomes a drag

    // ---- config-derived metrics -------------------------------------------
    private DockConfig _config = new();
    private MagnificationEngine _engine = null!;
    private double _iconSize;
    private double _spacing;
    private bool _showClock;

    private double _panelTopY;
    private double _iconBaselineY; // y of every icon's *bottom* edge (scale origin)
    private double _panelBottomY;
    private double _panelHeight;
    private double _windowHeight;
    private double _windowWidthDip;

    // ---- live state --------------------------------------------------------
    private readonly List<DockIcon> _icons = new();
    private double _cursorX;
    private bool _cursorActive;

    // Drag-to-reorder.
    private DockIcon? _pendingDrag;
    private DockIcon? _draggingIcon;
    private Point _dragStart;
    private double _dragCursorX;
    private bool _isDragging;

    // Clock.
    private TextBlock? _clockTime;
    private TextBlock? _clockBattery;
    private TextBlock? _clockDate;
    private Border? _clockHost;
    private DispatcherTimer? _clockTimer;

    private ControlCenterWindow? _controlCenter;
    private string _ownExeFileName = "KairosDock.exe";

    /// <summary>
    /// Windows host/shell processes that have visible top-level windows but are not
    /// user apps — they should never appear as dock icons.
    /// </summary>
    private static readonly HashSet<string> SystemProcessBlocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "applicationframehost.exe",  // hosts UWP app frames (shows generic gear)
        "textinputhost.exe",         // touch keyboard / input host
        "shellexperiencehost.exe",
        "startmenuexperiencehost.exe",
        "searchhost.exe",
        "searchapp.exe",
        "systemsettings.exe",
        "lockapp.exe",
        "widgets.exe",
        "widgetservice.exe",
    };

    // Hover previews (Windows-taskbar-style live thumbnails).
    private PreviewWindow? _preview;
    private DispatcherTimer? _hoverTimer;
    private DispatcherTimer? _previewHideTimer;
    private DockIcon? _hoverIcon;

    // The whole dock slides on this transform (entrance + auto-hide).
    private readonly TranslateTransform _dockSlide = new(0, 0);
    private readonly Spring _slideSpring = new(DockTuning.SlideStiffness, DockTuning.SlideDamping);
    private double _slideTarget;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrameTime;
    private double _dt; // last frame delta, shared with LayoutFrame's position springs
    private double _entranceStart;

    private DispatcherTimer? _runningTimer;
    private DispatcherTimer? _autoHideTimer;

    public MainWindow()
    {
        InitializeComponent();

        RootCanvas.RenderTransform = _dockSlide;

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    // =======================================================================
    //  Startup
    // =======================================================================

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Keep the dock out of Alt-Tab and stop it stealing focus when clicked.
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);

        // Optional real acrylic blur (opt-in via config). It blurs the whole window
        // rectangle, so it's off by default; the layered glass already looks premium.
        if (_config.Appearance.Blur)
        {
            try { AcrylicGlass.TryEnable(this); }
            catch (Exception ex2) { Debug.WriteLine($"[KairosDock] blur unavailable: {ex2.Message}"); }
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _config = ConfigService.Load();
        _ownExeFileName = System.IO.Path.GetFileName(
            Process.GetCurrentProcess().MainModule?.FileName ?? "KairosDock.exe");
        ApplyConfig();

        // Shell integration: optional auto-start + hide the Windows taskbar so the
        // Kairos dock is THE shell. The taskbar is restored when we exit.
        ShellIntegration.SetAutoStart(_config.AutoStart);
        if (_config.HideWindowsTaskbar)
        {
            ShellIntegration.HideTaskbar();
            Closed += (_, _) => ShellIntegration.RestoreTaskbar();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => ShellIntegration.RestoreTaskbar();
        }

        BuildIcons();
        if (_showClock)
            BuildClock();
        ComputeMetrics();
        RecomputeRestCenters();
        PositionWindow();
        LayoutFrame(initial: true);

        // The single source of truth for all motion.
        CompositionTarget.Rendering += OnRendering;

        StartRunningWatcher();
        PlayEntrance();

        if (_config.AutoHide)
            StartAutoHide();
    }

    private void ApplyConfig()
    {
        var a = _config.Appearance;
        _iconSize = a.IconSize;
        _spacing = a.IconSpacing;
        _showClock = a.ShowClock;
        // Config overrides the tuning defaults for the magnification curve.
        _engine = new MagnificationEngine(a.MaxScale, a.Influence);
        GlassPanel.CornerRadius = new CornerRadius(a.CornerRadius);

        if (a.Blur)
        {
            // With real blur behind, drop the heavy WPF shadow and lean the panel
            // more translucent so the blur reads through.
            GlassPanel.Effect = null;
        }
    }

    // =======================================================================
    //  Building visuals
    // =======================================================================

    private void BuildIcons()
    {
        foreach (var item in _config.Items)
            _icons.Add(CreateIcon(item));
    }

    private DockIcon CreateIcon(DockItem item)
    {
        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);

        // Built-in items get custom-drawn, on-brand icons; everything else pulls
        // artwork from its executable (with a letter tile as a last resort).
        UIElement content = item.Path switch
        {
            "kairos:start-menu" => CreateStartTile(),
            "kairos:ai" => CreateAiTile(),
            _ => ResolveIcon(item) is { } src ? CreateImage(src) : CreateFallbackTile(item.Name),
        };

        var host = new Border
        {
            Width = _iconSize,
            Height = _iconSize,
            Background = Brushes.Transparent, // whole cell clickable
            Child = content,
            Cursor = Cursors.Hand,
            // No native tooltip: the hover preview card already shows the app name,
            // so a tooltip would duplicate the label ("two apps" look). The name
            // lives on the DockIcon for the preview/context menu.
            RenderTransform = group,
            RenderTransformOrigin = new Point(0.5, 1.0), // grow upward from bottom-centre
            Opacity = 0, // faded in by the entrance / add animation
        };

        // Soft-glowing violet running dot.
        var dot = new Ellipse
        {
            Width = DotSize,
            Height = DotSize,
            Fill = (Brush)FindResource("KairosSoftPurpleBrush"),
            Opacity = 0,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                Color = ((SolidColorBrush)FindResource("KairosAccentBrush")).Color,
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 1.0,
            },
        };

        var dockIcon = new DockIcon(item, host, scale, translate, dot);

        host.MouseLeftButtonDown += (_, e2) => OnIconMouseDown(dockIcon, e2);
        host.MouseRightButtonUp += (_, e2) => { e2.Handled = true; ShowContextMenu(dockIcon); };
        host.MouseEnter += (_, _) => OnIconHover(dockIcon, true);
        host.MouseLeave += (_, _) => OnIconHover(dockIcon, false);

        IconLayer.Children.Add(dot);
        IconLayer.Children.Add(host);
        return dockIcon;
    }

    /// <summary>Explicit icon file if given, else pulled from the executable.</summary>
    private static BitmapSource? ResolveIcon(DockItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Icon) && System.IO.File.Exists(item.Icon))
        {
            var fromFile = IconExtractor.FromImageFile(item.Icon!);
            if (fromFile != null)
                return fromFile;
        }
        return IconExtractor.FromPath(item.Path);
    }

    private Image CreateImage(BitmapSource source)
    {
        var image = new Image
        {
            Width = _iconSize,
            Height = _iconSize,
            Source = source,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
        };
        // Crisp at every magnified scale.
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    /// <summary>A rounded violet gradient tile showing the app's initial.</summary>
    private Border CreateFallbackTile(string name)
    {
        string letter = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();
        return new Border
        {
            Width = _iconSize,
            Height = _iconSize,
            CornerRadius = new CornerRadius(_iconSize * 0.24),
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(
                ((SolidColorBrush)FindResource("KairosAccentBrush")).Color,
                ((SolidColorBrush)FindResource("KairosSoftPurpleBrush")).Color,
                new Point(0, 0), new Point(1, 1)),
            Child = new TextBlock
            {
                Text = letter,
                Foreground = Brushes.White,
                FontSize = _iconSize * 0.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>"Start" / OS-logo tile: a 2×2 grid of rounded violet squares.</summary>
    private UIElement CreateStartTile()
    {
        var accent = ((SolidColorBrush)FindResource("KairosAccentBrush")).Color;
        var soft = ((SolidColorBrush)FindResource("KairosSoftPurpleBrush")).Color;

        var grid = new System.Windows.Controls.Primitives.UniformGrid
        {
            Rows = 2,
            Columns = 2,
            Width = _iconSize * 0.62,
            Height = _iconSize * 0.62,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        for (int i = 0; i < 4; i++)
        {
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(_iconSize * 0.07),
                Margin = new Thickness(_iconSize * 0.03),
                Background = new SolidColorBrush(i is 0 or 3 ? soft : accent),
            });
        }
        return new Border { Width = _iconSize, Height = _iconSize, Child = grid, IsHitTestVisible = false };
    }

    /// <summary>"AI" tile: a soft violet four-point sparkle.</summary>
    private UIElement CreateAiTile()
    {
        var accent = ((SolidColorBrush)FindResource("KairosAccentBrush")).Color;
        var soft = ((SolidColorBrush)FindResource("KairosSoftPurpleBrush")).Color;

        var sparkle = new Path
        {
            // A concave 4-point star (sparkle).
            Data = Geometry.Parse("M 12,0 C 13,7 17,11 24,12 C 17,13 13,17 12,24 C 11,17 7,13 0,12 C 7,11 11,7 12,0 Z"),
            Stretch = Stretch.Uniform,
            Width = _iconSize * 0.72,
            Height = _iconSize * 0.72,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = new LinearGradientBrush(soft, accent, new Point(0, 0), new Point(1, 1)),
            Effect = new DropShadowEffect { Color = accent, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.7 },
        };
        return new Border { Width = _iconSize, Height = _iconSize, Child = sparkle, IsHitTestVisible = false };
    }

    private void BuildClock()
    {
        _clockTime = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        // Battery, just below the time (icon glyph + percent).
        _clockBattery = new TextBlock
        {
            FontSize = 10,
            Foreground = (Brush)FindResource("KairosSoftPurpleBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        _clockDate = new TextBlock
        {
            Foreground = (Brush)FindResource("KairosSoftPurpleBrush"),
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Opacity = 0, // revealed on hover
        };

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(_clockTime);
        stack.Children.Add(_clockBattery);
        stack.Children.Add(_clockDate);

        _clockHost = new Border
        {
            Width = ClockWidth,
            Background = Brushes.Transparent,
            Child = stack,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        // Reveal the date on hover; click opens the Control Center.
        _clockHost.MouseEnter += (_, _) => FadeTo(_clockDate, 1, 180);
        _clockHost.MouseLeave += (_, _) => FadeTo(_clockDate, 0, 180);
        _clockHost.MouseLeftButtonUp += (_, e2) => { e2.Handled = true; ToggleControlCenter(); };

        IconLayer.Children.Add(_clockHost);

        UpdateClock();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        if (_clockTime != null) _clockTime.Text = now.ToString("HH:mm");
        if (_clockDate != null) _clockDate.Text = now.ToString("ddd d MMM");

        if (_clockBattery != null)
        {
            var bat = SystemStatus.GetBattery();
            // MDL2 battery glyph + percent; a bolt while charging. Hidden on desktops.
            _clockBattery.Visibility = bat.Present ? Visibility.Visible : Visibility.Collapsed;
            if (bat.Present)
                _clockBattery.Text = bat.Charging ? $" {bat.Percent}% ⚡" : $" {bat.Percent}%";
        }
    }

    private void ToggleControlCenter()
    {
        _controlCenter ??= new ControlCenterWindow();

        // If the panel was open, clicking the clock first deactivated it (auto-hide)
        // — so by now it's already hidden. Detect that "just closed by this click"
        // case and do nothing, instead of immediately reopening it.
        if ((DateTime.Now - _controlCenter.LastHidden).TotalMilliseconds < 280)
            return;
        if (_controlCenter.IsVisible)
        {
            _controlCenter.Hide();
            return;
        }

        // Anchor over the clock, in DIPs (Window.Left/Top space) — no DPI maths.
        double anchorX = Left + (_clockHost != null ? Canvas.GetLeft(_clockHost) + (ClockWidth / 2.0) : Width / 2.0);
        double dockTopY = Top + _panelTopY;
        _controlCenter.Open(anchorX, dockTopY);
    }

    // =======================================================================
    //  Geometry
    // =======================================================================

    private double ClockTotal => _showClock ? ClockGap + ClockWidth : 0;

    private void ComputeMetrics()
    {
        double maxIconH = _iconSize * _engine.MaxScale;
        double overflowAbove = maxIconH - _iconSize;

        _panelHeight = _iconSize + (2 * PanelPadY) + DotRoom;
        _panelTopY = ShadowMargin + overflowAbove;
        _iconBaselineY = _panelTopY + PanelPadY + _iconSize;
        _panelBottomY = _panelTopY + _panelHeight;
        _windowHeight = _panelBottomY + ShadowMargin;

        // Icons PUSH APART when magnified (macOS-style) so they never overlap, so
        // the window must be wide enough for the fully-expanded row.
        int n = _icons.Count;
        double maxContent = (n * _iconSize * _engine.MaxScale) + (Math.Max(0, n - 1) * _spacing) + ClockTotal;
        _windowWidthDip = maxContent + (2 * PanelPadX) + (2 * ShadowMargin);
    }

    /// <summary>Rest centres anchor the magnification falloff; recomputed on reorder.</summary>
    private void RecomputeRestCenters()
    {
        int n = _icons.Count;
        double iconsRest = (n * _iconSize) + (Math.Max(0, n - 1) * _spacing);
        double restPanelW = iconsRest + ClockTotal + (2 * PanelPadX);
        double restLeft = (_windowWidthDip - restPanelW) / 2.0;

        for (int i = 0; i < n; i++)
            _icons[i].RestCenter = restLeft + PanelPadX + (i * (_iconSize + _spacing)) + (_iconSize / 2.0);
    }

    private void PositionWindow()
    {
        var wa = SystemParameters.WorkArea;
        Width = _windowWidthDip;
        Left = wa.Left + ((wa.Width - _windowWidthDip) / 2.0);
        Height = _windowHeight;
        // When we've hidden the Windows taskbar, anchor to the true screen bottom
        // (the work area still excludes the now-hidden taskbar's strip).
        double bottom = _config.HideWindowsTaskbar ? SystemParameters.PrimaryScreenHeight : wa.Bottom;
        Top = bottom - _config.Appearance.BottomMargin - _panelBottomY;
    }

    // =======================================================================
    //  The animation loop — the core of the "feel"
    // =======================================================================

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = now - _lastFrameTime;
        _lastFrameTime = now;
        if (dt <= 0)
            return;
        _dt = dt;

        double sinceEntrance = now - _entranceStart;

        _dockSlide.Y = _slideSpring.Step(_slideTarget, dt);

        // Step every icon's springs. Each value keeps gliding for a beat after the
        // cursor stops — that trailing settle is what reads as physical.
        for (int i = _icons.Count - 1; i >= 0; i--)
        {
            var icon = _icons[i];

            // Magnification target from the smooth Gaussian falloff.
            double target = _engine.ScaleFor(icon.RestCenter, _cursorX, _cursorActive);
            icon.ScaleSpring.Step(target, dt);

            // Entrance pop (staggered) / removal collapse.
            if (!icon.Started && sinceEntrance >= icon.EntranceDelay)
                icon.Started = true;
            if (icon.Started)
            {
                double popTarget = icon.Removing ? 0.0 : 1.0;
                icon.PopSpring.Step(popTarget, dt);
            }

            // Launch bounce settles back to 0.
            icon.Translate.Y = icon.BounceSpring.Step(0, dt);

            // Apply the composite scale + opacity.
            double s = icon.CurrentScale;
            icon.Scale.ScaleX = s;
            icon.Scale.ScaleY = s;
            icon.Host.Opacity = Math.Clamp(icon.PopSpring.Value, 0, 1);

            // Finished collapsing → actually remove.
            if (icon.Removing && icon.PopSpring.Value <= 0.02)
                FinalizeRemoval(icon);
        }

        LayoutFrame(initial: false);
    }

    /// <summary>
    /// Position the glass panel, icons (with smoothly-springing positions so the
    /// row flows and reorders ease) and the clock for the current frame.
    /// </summary>
    private void LayoutFrame(bool initial)
    {
        int n = _icons.Count;

        // Panel width = the summed *scaled* icon widths (+ clock), so the row grows
        // as icons magnify and they push each other apart instead of overlapping.
        double content = 0;
        for (int i = 0; i < n; i++)
            content += _iconSize * _icons[i].ScaleSpring.Value;
        content += (Math.Max(0, n - 1) * _spacing) + ClockTotal;

        double panelW = content + (2 * PanelPadX);
        double panelLeft = (_windowWidthDip - panelW) / 2.0;

        GlassPanel.Width = panelW;
        GlassPanel.Height = _panelHeight;
        Canvas.SetLeft(GlassPanel, panelLeft);
        Canvas.SetTop(GlassPanel, _panelTopY);

        // Lay icons left-to-right by their scaled widths; each rendered centre eases
        // (position spring) toward its slot so neighbours glide apart smoothly and a
        // magnified icon never lands on top of another.
        double x = panelLeft + PanelPadX;
        for (int i = 0; i < n; i++)
        {
            var icon = _icons[i];
            double scaledW = _iconSize * icon.ScaleSpring.Value;
            double targetCenter = x + (scaledW / 2.0);

            double center;
            if (icon == _draggingIcon)
            {
                center = _dragCursorX;
                icon.PosSpring.Reset(center);
            }
            else if (!icon.PosInitialized)
            {
                center = targetCenter;
                icon.PosSpring.Reset(targetCenter);
                icon.PosInitialized = true;
            }
            else
            {
                center = icon.PosSpring.Step(targetCenter, _dt);
            }

            Canvas.SetLeft(icon.Host, center - (_iconSize / 2.0));
            Canvas.SetTop(icon.Host, _iconBaselineY - _iconSize);

            Canvas.SetLeft(icon.Dot, center - (DotSize / 2.0));
            Canvas.SetTop(icon.Dot, _iconBaselineY + ((DotRoom - DotSize) / 2.0) + 2);

            x += scaledW;
            if (i < n - 1) x += _spacing;
        }

        // Clock sits just after the row.
        if (_clockHost != null)
        {
            Canvas.SetLeft(_clockHost, x + ClockGap);
            Canvas.SetTop(_clockHost, _iconBaselineY - _iconSize);
            _clockHost.Height = _iconSize;
        }
    }

    // =======================================================================
    //  Input — click, drag-to-reorder
    // =======================================================================

    private void OnIconMouseDown(DockIcon icon, MouseButtonEventArgs e)
    {
        _pendingDrag = icon;
        _dragStart = e.GetPosition(RootCanvas);
    }

    // --- hover previews ----------------------------------------------------

    private void OnIconHover(DockIcon icon, bool entering)
    {
        if (_isDragging)
            return;

        if (!entering)
        {
            // Don't hide immediately — give the cursor a grace period to travel
            // onto the preview (to reach its close button), Windows-style.
            if (_hoverIcon == icon)
            {
                _hoverIcon = null;
                _hoverTimer?.Stop();
                SchedulePreviewHide();
            }
            return;
        }

        _hoverIcon = icon;
        StopPreviewHide();

        // Only running apps with a real window get a preview.
        if (!icon.IsRunning || icon.Window == IntPtr.Zero)
        {
            _preview?.HidePreview();
            return;
        }

        // Small delay before the preview appears (avoids flicker while sweeping).
        _hoverTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _hoverTimer.Stop();
        _hoverTimer.Tick -= OnHoverTick;
        _hoverTimer.Tick += OnHoverTick;
        _hoverTimer.Start();
    }

    private void OnHoverTick(object? sender, EventArgs e)
    {
        _hoverTimer?.Stop();
        var icon = _hoverIcon;
        if (icon == null || !icon.IsRunning || icon.Window == IntPtr.Zero)
            return;

        EnsurePreview();
        double centerX = Left + Canvas.GetLeft(icon.Host) + (_iconSize / 2.0);
        double topY = Top + _panelTopY - 8; // just above the dock's glass
        _preview!.ShowFor(icon.Window, centerX, topY, icon.Item.Name);
    }

    private void EnsurePreview()
    {
        if (_preview != null)
            return;
        _preview = new PreviewWindow();
        // Keep the preview open while the cursor is over it (so the X is reachable).
        _preview.MouseEnter += (_, _) => StopPreviewHide();
        _preview.MouseLeave += (_, _) => SchedulePreviewHide();
        _preview.AppClosed += () => UpdateRunningStates(); // reflect the close at once
    }

    private void SchedulePreviewHide()
    {
        _previewHideTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        _previewHideTimer.Stop();
        _previewHideTimer.Tick -= OnPreviewHideTick;
        _previewHideTimer.Tick += OnPreviewHideTick;
        _previewHideTimer.Start();
    }

    private void StopPreviewHide() => _previewHideTimer?.Stop();

    private void OnPreviewHideTick(object? sender, EventArgs e)
    {
        _previewHideTimer?.Stop();
        _preview?.HidePreview();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(RootCanvas);
        _cursorX = p.X;
        _cursorActive = true;

        if (_pendingDrag == null)
            return;

        if (!_isDragging)
        {
            // Promote a press into a drag once the pointer moves far enough.
            if ((p - _dragStart).Length >= DragThreshold)
                BeginDrag();
        }

        if (_isDragging)
        {
            _dragCursorX = p.X;
            ReorderDuringDrag();
        }
    }

    private void BeginDrag()
    {
        _isDragging = true;
        _draggingIcon = _pendingDrag;
        if (_draggingIcon != null)
        {
            Panel.SetZIndex(_draggingIcon.Host, 100); // float above the others
            _draggingIcon.Host.Opacity = 1;
        }
        CaptureMouse();
    }

    private void ReorderDuringDrag()
    {
        if (_draggingIcon == null)
            return;

        // Desired index = how many *other* icons have their centre left of the cursor.
        int desired = 0;
        foreach (var icon in _icons)
        {
            if (icon == _draggingIcon)
                continue;
            if (icon.PosSpring.Value < _dragCursorX)
                desired++;
        }

        int current = _icons.IndexOf(_draggingIcon);
        desired = Math.Clamp(desired, 0, _icons.Count - 1);
        if (desired != current)
        {
            _icons.RemoveAt(current);
            _icons.Insert(desired, _draggingIcon);
            RecomputeRestCenters(); // others glide to new slots via their PosSpring
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            EndDrag();
        }
        else if (_pendingDrag != null)
        {
            // A clean click (no drag) → launch / focus, with a bounce.
            LaunchWithBounce(_pendingDrag);
        }
        _pendingDrag = null;
    }

    private void EndDrag()
    {
        if (_draggingIcon != null)
            Panel.SetZIndex(_draggingIcon.Host, 0);

        _isDragging = false;
        _draggingIcon = null;
        ReleaseMouseCapture();

        // Persist the new order — but only the *pinned* items (transient running
        // apps must never leak into the config).
        _config.Items = _icons.Where(i => i.Item.Pinned).Select(i => i.Item).ToList();
        ConfigService.Save(_config);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
            _cursorActive = false; // relax icons back to rest
    }

    private void LaunchWithBounce(DockIcon icon)
    {
        // Upward velocity impulse on an under-damped spring → a couple of soft hops.
        icon.BounceSpring.Nudge(DockTuning.BounceImpulse);

        switch (icon.Item.Path)
        {
            case "kairos:start-menu":
                NativeMethods.OpenStartMenu();
                break;
            case "kairos:ai":
                // The system AI. Defaults to Copilot on the web (works everywhere);
                // change this target in the action below to point at your own AI.
                AppLauncher.Launch(new DockItem { Path = "https://copilot.microsoft.com/" });
                break;
            default:
                AppLauncher.LaunchOrFocus(icon.Item);
                break;
        }
    }

    // =======================================================================
    //  Context menu
    // =======================================================================

    private void ShowContextMenu(DockIcon icon)
    {
        var menu = new ContextMenu();

        if (icon.IsRunning)
        {
            var quit = new MenuItem { Header = $"Cerrar “{icon.Item.Name}”" };
            quit.Click += (_, _) => AppLauncher.Quit(icon.Item);
            menu.Items.Add(quit);
            menu.Items.Add(new Separator());
        }

        if (icon.Item.Pinned)
        {
            // Pinned → can be removed (which also unpins + persists).
            var remove = new MenuItem { Header = "Quitar del dock" };
            remove.Click += (_, _) => RemoveIcon(icon);
            menu.Items.Add(remove);
        }
        else
        {
            // Transient running app → offer to pin it so it stays after closing.
            var keep = new MenuItem { Header = "Mantener en el dock" };
            keep.Click += (_, _) => PinIcon(icon);
            menu.Items.Add(keep);
        }

        menu.PlacementTarget = icon.Host;
        menu.IsOpen = true;
    }

    /// <summary>Pin a transient (auto-added) app so it persists in the dock.</summary>
    private void PinIcon(DockIcon icon)
    {
        icon.Item.Pinned = true;
        if (!_config.Items.Contains(icon.Item))
            _config.Items.Add(icon.Item);
        ConfigService.Save(_config);
    }

    /// <summary>Start the collapse animation; the loop removes it when it finishes.</summary>
    private void RemoveIcon(DockIcon icon)
    {
        icon.Item.Pinned = false; // so the reconciler doesn't treat it as pinned
        icon.Removing = true;
        FadeTo(icon.Dot, 0, 150);

        _config.Items.Remove(icon.Item);
        ConfigService.Save(_config);
    }

    private void FinalizeRemoval(DockIcon icon)
    {
        IconLayer.Children.Remove(icon.Host);
        IconLayer.Children.Remove(icon.Dot);
        _icons.Remove(icon);
        ComputeMetrics();
        PositionWindow();
        RecomputeRestCenters();
    }

    // =======================================================================
    //  Entrance + auto-hide motion
    // =======================================================================

    private void PlayEntrance()
    {
        _entranceStart = _clock.Elapsed.TotalSeconds;

        // Slide the whole dock up and fade in.
        _slideSpring.Reset(_panelHeight + 44);
        _slideTarget = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });

        // Stagger each icon's pop-in.
        for (int i = 0; i < _icons.Count; i++)
        {
            _icons[i].EntranceDelay = 0.12 + (i * DockTuning.EntranceStaggerStep);
            _icons[i].PopSpring.Reset(0);
        }
    }

    private void StartAutoHide()
    {
        double hideDistance = _windowHeight;

        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _autoHideTimer.Tick += (_, _) =>
        {
            if (!NativeMethods.GetCursorPos(out NativeMethods.POINT p))
                return;
            var wa = SystemParameters.WorkArea;
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
            double bottomPx = wa.Bottom * dpi;
            bool nearBottom = p.Y >= bottomPx - 3;
            _slideTarget = nearBottom ? 0 : hideDistance;
        };
        _autoHideTimer.Start();
        _slideTarget = hideDistance; // start hidden
    }

    // =======================================================================
    //  Running-app indicator
    // =======================================================================

    private void StartRunningWatcher()
    {
        UpdateRunningStates();
        _runningTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _runningTimer.Tick += (_, _) => UpdateRunningStates();
        _runningTimer.Start();
    }

    /// <summary>
    /// Reconciles the dock with what's actually running, Windows-taskbar-style:
    /// pinned items always stay (their dot reflects running state); apps that open
    /// without a pinned icon get a transient icon that pops in, and pops out when
    /// they close.
    /// </summary>
    private void UpdateRunningStates()
    {
        // One window-enumeration snapshot per tick (collapsed to one entry per exe).
        var snapshot = RunningApps.Snapshot();

        // Running apps keyed by a GROUP key (product name when distinctive, else
        // exe file name). Grouping by product merges multi-process apps into one
        // icon — e.g. VirtualBox's Manager + the running VM become a single icon.
        var runningByName = new Dictionary<string, RunningApps.Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Values)
        {
            string fn = System.IO.Path.GetFileName(entry.ExePath);
            if (fn.Length == 0 || fn.Equals(_ownExeFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            // Skip Windows' own host/shell processes — they have alt-tab windows but
            // aren't apps the user launched (e.g. the input host, the UWP frame host).
            if (SystemProcessBlocklist.Contains(fn))
                continue;
            string key = GroupKeyForPath(entry.ExePath);
            if (!runningByName.ContainsKey(key))
                runningByName[key] = entry; // keep the first window of the group
        }

        // 1. Update running dots + window handles, and note covered groups.
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var icon in _icons)
        {
            string fn = ItemGroupKey(icon.Item);
            if (fn.Length > 0)
                covered.Add(fn);

            RunningApps.Entry entry = default;
            bool running = fn.Length > 0 && runningByName.TryGetValue(fn, out entry);
            icon.Window = running ? entry.Window : IntPtr.Zero;
            if (running != icon.IsRunning)
            {
                icon.IsRunning = running;
                FadeTo(icon.Dot, running ? 1.0 : 0.0, 240);
            }
        }

        bool changed = false;

        // 2. Add a transient icon for any running app the dock doesn't cover yet.
        foreach (var (fn, entry) in runningByName)
        {
            if (covered.Contains(fn))
                continue;
            AddTransientIcon(entry.ExePath, entry.Window);
            covered.Add(fn);
            changed = true;
        }

        // 3. Collapse transient icons whose app has closed.
        foreach (var icon in _icons)
        {
            if (icon.Item.Pinned || icon.Removing)
                continue;
            string fn = ItemGroupKey(icon.Item);
            if (!runningByName.ContainsKey(fn))
            {
                icon.Removing = true;
                FadeTo(icon.Dot, 0, 150);
                // Geometry shrinks once FinalizeRemoval runs in the loop.
            }
        }

        if (changed)
        {
            ComputeMetrics();
            PositionWindow();
            RecomputeRestCenters();
        }
    }

    /// <summary>Adds an auto-discovered running app to the dock (pops in).</summary>
    private void AddTransientIcon(string exePath, IntPtr window)
    {
        var item = new DockItem
        {
            Name = NiceName(exePath),
            Path = exePath,
            Pinned = false,
        };
        var icon = CreateIcon(item);
        icon.IsRunning = true;
        icon.Window = window;
        icon.Started = true;          // begin popping in immediately
        icon.PopSpring.Reset(0);
        FadeTo(icon.Dot, 1.0, 240);
        _icons.Add(icon);
    }

    /// <summary>The grouping key a dock item maps to, or "" for special / URL items.</summary>
    private static string ItemGroupKey(DockItem item)
    {
        string p = item.Path;
        if (string.IsNullOrWhiteSpace(p) || p.StartsWith("kairos:", StringComparison.OrdinalIgnoreCase)
            || p.Contains("://"))
            return "";
        try { return GroupKeyForPath(Environment.ExpandEnvironmentVariables(p)); }
        catch { return ""; }
    }

    /// <summary>
    /// Groups apps by product name when it's distinctive (so VirtualBox's two
    /// executables collapse to one icon), falling back to the exe file name. The
    /// generic "…Windows… Operating System" product is treated per-exe so system
    /// apps (Explorer, Settings…) don't all merge together.
    /// </summary>
    private static string GroupKeyForPath(string fullPath)
    {
        try
        {
            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(fullPath);
            string? prod = fvi.ProductName?.Trim();
            if (!string.IsNullOrEmpty(prod) &&
                !(prod.Contains("Windows", StringComparison.OrdinalIgnoreCase) &&
                  prod.Contains("Operating System", StringComparison.OrdinalIgnoreCase)))
                return prod!;
        }
        catch { /* ignore */ }
        return System.IO.Path.GetFileName(fullPath);
    }

    /// <summary>A friendly app name from its file metadata (falls back to the file name).</summary>
    private static string NiceName(string exePath)
    {
        try
        {
            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrWhiteSpace(fvi.FileDescription))
                return fvi.FileDescription!;
        }
        catch { /* ignore */ }
        return System.IO.Path.GetFileNameWithoutExtension(exePath);
    }

    private static void FadeTo(UIElement? el, double to, int ms)
    {
        el?.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }
}
