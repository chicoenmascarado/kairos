using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Windows.Devices.Radios;
using KairosDock.Interop;
using KairosDock.Services;

namespace KairosDock;

/// <summary>
/// An Apple-style "Control Center" that drops out of the dock's clock: battery,
/// volume + brightness sliders, and a list of background apps you can click to
/// focus. It's a separate, activatable window (unlike the no-activate dock) so the
/// sliders actually receive drag input; it hides itself when it loses focus.
/// </summary>
public partial class ControlCenterWindow : Window
{
    private bool _suppress; // guards slider ValueChanged while we refresh values
    private bool _closing;  // true while the close animation is playing
    private bool _trayInteracting;          // a tray icon was clicked → keep panel up
    private DateTime _suppressHideUntil = DateTime.MinValue;

    public ControlCenterWindow()
    {
        InitializeComponent();

        // While interacting with a tray icon's menu the panel stays open; it then
        // closes once the cursor leaves it.
        MouseLeave += (_, _) => { if (_trayInteracting) ForceHide(); };

        VolumeSlider.ValueChanged += (_, _) =>
        {
            if (!_suppress) SystemStatus.SetVolume((float)(VolumeSlider.Value / 100.0));
        };
        BrightnessSlider.ValueChanged += (_, _) =>
        {
            if (!_suppress) SystemStatus.SetBrightness((int)BrightnessSlider.Value);
        };

        // Click-away closes it (with a quick fade-down). Record when, so the dock
        // can tell "click the clock to close" from a fresh open (and not reopen).
        Deactivated += (_, _) => HideAnimated();

        // Task Manager quick action.
        var hover = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        var rest = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        TaskMgrButton.MouseEnter += (_, _) => TaskMgrButton.Background = hover;
        TaskMgrButton.MouseLeave += (_, _) => TaskMgrButton.Background = rest;
        TaskMgrButton.MouseLeftButtonUp += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); }
            catch (Exception ex) { Debug.WriteLine($"[KairosDock] taskmgr failed: {ex.Message}"); }
            Hide();
        };

        // Battery in the header → open power & battery settings.
        BatteryPanel.Background = System.Windows.Media.Brushes.Transparent; // hit-testable
        BatteryPanel.Cursor = Cursors.Hand;
        BatteryPanel.MouseLeftButtonUp += (_, _) => { Hide(); LaunchShell("ms-settings:batterysaver"); };

        BuildExtras();
    }

    private Border? _darkPill;
    private Border? _wifiPill;
    private Border? _btPill;
    private TextBlock? _playPauseBtn;

    private void BuildExtras()
    {
        // Apple-style quick toggles — all real instant toggles.
        _darkPill = MakeToggle(0xE793, "Oscuro", () => { SystemStatus.SetDarkMode(!SystemStatus.IsDarkMode()); UpdateDarkPill(); });
        _wifiPill = MakeToggle(0xE701, "Wi-Fi", () => ToggleRadio(RadioKind.WiFi));
        _btPill = MakeToggle(0xE702, "Bluetooth", () => ToggleRadio(RadioKind.Bluetooth));
        TogglesRow.Children.Add(_darkPill);
        TogglesRow.Children.Add(_wifiPill);
        TogglesRow.Children.Add(_btPill);
        UpdateDarkPill();

        // Media transport — global media keys (works with Spotify, YouTube…). After
        // each press we re-read the SMTC state to update the title + play/pause icon.
        MediaButtons.Children.Add(MakeMediaButton(0xE892, () => { SystemStatus.MediaPrevious(); UpdateMediaSoon(); }));
        _playPauseBtn = MakeMediaButton(0xE768, () => { SystemStatus.MediaPlayPause(); UpdateMediaSoon(); });
        MediaButtons.Children.Add(_playPauseBtn);
        MediaButtons.Children.Add(MakeMediaButton(0xE893, () => { SystemStatus.MediaNext(); UpdateMediaSoon(); }));
    }

    private async void ToggleRadio(RadioKind kind)
    {
        await RadioControl.ToggleAsync(kind);
        await Task.Delay(250); // let the radio settle, then reflect the new state
        UpdateRadioPills();
    }

    private async void UpdateRadioPills()
    {
        bool? wifi = await RadioControl.IsOnAsync(RadioKind.WiFi);
        bool? bt = await RadioControl.IsOnAsync(RadioKind.Bluetooth);
        ColorPill(_wifiPill, wifi == true);
        ColorPill(_btPill, bt == true);
    }

    private void ColorPill(Border? pill, bool on)
    {
        if (pill != null)
            pill.Background = on ? (Brush)FindResource("KairosAccentBrush") : SubtleBrush();
    }

    private void UpdateMediaSoon()
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        t.Tick += (_, _) => { t.Stop(); UpdateMedia(); };
        t.Start();
    }

    private async void UpdateMedia()
    {
        var np = await MediaControl.GetAsync();
        if (!np.HasSession || string.IsNullOrWhiteSpace(np.Title))
        {
            MediaTitle.Visibility = Visibility.Collapsed;
        }
        else
        {
            MediaTitle.Visibility = Visibility.Visible;
            MediaTitle.Text = string.IsNullOrWhiteSpace(np.Artist) ? np.Title : $"{np.Title} — {np.Artist}";
        }
        // Playing → show pause glyph; paused → show play glyph.
        if (_playPauseBtn != null)
            _playPauseBtn.Text = char.ConvertFromUtf32(np.Playing ? 0xE769 : 0xE768);
    }

    private Border MakeToggle(int glyphCode, string label, Action onClick)
    {
        var g = new TextBlock
        {
            Text = char.ConvertFromUtf32(glyphCode), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 17,
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
        };
        var t = new TextBlock
        {
            Text = label, FontSize = 10, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0),
        };
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(g);
        sp.Children.Add(t);
        var pill = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = SubtleBrush(),
            Margin = new Thickness(4),
            Height = 56,
            Cursor = Cursors.Hand,
            Child = sp,
        };
        pill.MouseLeftButtonUp += (_, _) => onClick();
        return pill;
    }

    private TextBlock MakeMediaButton(int glyphCode, Action onClick)
    {
        var b = new TextBlock
        {
            Text = char.ConvertFromUtf32(glyphCode), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 20,
            Foreground = Brushes.White, Margin = new Thickness(18, 4, 18, 4), Cursor = Cursors.Hand,
        };
        b.MouseLeftButtonUp += (_, _) => onClick();
        return b;
    }

    private void UpdateDarkPill()
    {
        if (_darkPill == null) return;
        _darkPill.Background = SystemStatus.IsDarkMode()
            ? (Brush)FindResource("KairosAccentBrush")
            : SubtleBrush();
    }

    private static SolidColorBrush SubtleBrush() => new(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

    /// <summary>When the panel last hid itself (for the dock's open/close toggle).</summary>
    public DateTime LastHidden { get; private set; } = DateTime.MinValue;

    /// <summary>
    /// Opens (and refreshes) the panel above the dock, with a fluid Apple-style
    /// rise. Coordinates are in DIPs (same space as Window.Left/Top), so we
    /// sidestep the physical-pixel/DIP mismatch entirely.
    /// </summary>
    public void Open(double anchorCenterXDip, double dockTopYDip)
    {
        _closing = false; // cancel any in-flight close so it can't hide us
        _trayInteracting = false;
        _suppressHideUntil = DateTime.MinValue;
        Refresh();

        // Stay invisible and pre-set the animation's start state BEFORE showing, so
        // there's no flash at the final position and no "jump".
        Opacity = 0;
        CardSlide.Y = 28;
        CardScale.ScaleX = CardScale.ScaleY = 0.94;

        var wa = SystemParameters.WorkArea;
        double left = anchorCenterXDip - (Width / 2.0);
        Left = Math.Max(wa.Left + 4, Math.Min(left, wa.Right - Width - 4));

        Show(); // SizeToContent forces a measure here, so ActualHeight is now valid…
        // …position synchronously (still invisible) — no visible jump.
        Top = dockTopYDip - (ActualHeight - 16) - 4;
        Activate();

        PlayOpenAnimation();
    }

    /// <summary>Force the panel closed regardless of the tray-interaction hold.</summary>
    private void ForceHide()
    {
        _trayInteracting = false;
        _suppressHideUntil = DateTime.MinValue;
        HideAnimated();
    }

    /// <summary>Quick fade-down close (Apple-style), then actually hide.</summary>
    private void HideAnimated()
    {
        // A tray click stole the foreground on purpose — keep the panel visible so
        // the app's menu shows alongside it (closes when the cursor leaves).
        if (DateTime.Now < _suppressHideUntil)
            return;

        LastHidden = DateTime.Now; // set immediately so the reclick guard works
        if (_closing || !IsVisible)
            return;
        _closing = true;

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (_closing) { _closing = false; Hide(); } // skip if we were re-opened
        };
        CardSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(0, 18, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        var sc = new DoubleAnimation(1, 0.97, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
        CardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sc);
        CardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sc);
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Slide-up + subtle scale + fade — the "rises out of the dock" feel.</summary>
    private void PlayOpenAnimation()
    {
        // A custom cubic-bezier close to macOS spring easing (gentle overshoot-free
        // deceleration) makes it read as fluid rather than linear.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slideEase = new QuinticEase { EasingMode = EasingMode.EaseOut };

        CardSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(360)) { EasingFunction = slideEase });

        var scale = new DoubleAnimation(0.94, 1.0, TimeSpan.FromMilliseconds(360)) { EasingFunction = slideEase };
        CardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        CardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);

        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
    }

    private void Refresh()
    {
        _suppress = true;

        // Clock.
        var now = DateTime.Now;
        TimeText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("dddd d MMM");

        // Battery.
        var bat = SystemStatus.GetBattery();
        if (bat.Present)
        {
            BatteryPanel.Visibility = Visibility.Visible;
            BatteryGlyph.Text = ""; // MDL2 battery glyph (explicit so it never blanks)
            BatteryText.Text = bat.Charging ? $"{bat.Percent}%  ⚡" : $"{bat.Percent}%";
        }
        else
        {
            BatteryPanel.Visibility = Visibility.Collapsed;
        }

        // Volume.
        float vol = SystemStatus.GetVolume();
        VolumeSlider.Value = vol < 0 ? 0 : vol * 100.0;

        // Brightness (hide the card if the machine can't report it).
        int br = SystemStatus.GetBrightness();
        if (SystemStatus.BrightnessAvailable && br >= 0)
        {
            BrightnessCard.Visibility = Visibility.Visible;
            BrightnessSlider.Value = br;
        }
        else
        {
            BrightnessCard.Visibility = Visibility.Collapsed;
        }

        PopulateTray();
        UpdateDarkPill();
        UpdateRadioPills(); // async: colours the Wi-Fi/Bluetooth pills by real state
        UpdateMedia();      // async: now-playing title + play/pause glyph

        _suppress = false;
    }

    private void PopulateTray()
    {
        TrayList.Children.Clear();
        var items = SystemTray.Enumerate();

        foreach (var it in items)
        {
            if (it.Icon == null)
                continue; // skip entries we couldn't resolve an icon for

            string tip = (it.Tooltip ?? "").ToLowerInvariant();

            // Sound + battery + network icons → removed (the panel covers all three:
            // volume slider, battery in the header, Wi-Fi toggle).
            if (IsVolumeTip(tip) || IsBatteryTip(tip) || IsNetworkTip(tip))
                continue;

            var item = it; // capture for the click handlers
            var img = new Image
            {
                Source = it.Icon,
                Width = 20,
                Height = 20,
                Margin = new Thickness(5, 4, 5, 4),
                Cursor = Cursors.Hand,
                // No tooltip — same as the dock icons (kept the UI clean).
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            // Third-party tray icons: forward the click to the owning app, so
            // left = activate and right = its own context menu. Panel stays open.
            img.MouseLeftButtonUp += (_, _) => ForwardTrayClick(item, rightClick: false);
            img.MouseRightButtonUp += (_, _) => ForwardTrayClick(item, rightClick: true);

            TrayList.Children.Add(img);
        }

        // Hide the whole section if the tray is empty (e.g. Windows 11's XAML tray).
        bool any = TrayList.Children.Count > 0;
        TrayHeader.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        TrayCard.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    // Identify the system volume / network icons by their tooltip (multi-language).
    private static bool IsVolumeTip(string tip) =>
        tip.Contains("volum") || tip.Contains("altavoces") || tip.Contains("sonido") ||
        tip.Contains("speaker") || tip.Contains("sound") || tip.Contains("audio");

    private static bool IsNetworkTip(string tip) =>
        tip.Contains("internet") || tip.Contains("wi-fi") || tip.Contains("wifi") ||
        tip.Contains("ethernet") || tip.Contains("network") || tip.Contains("conexi") ||
        tip.Contains("red ") || tip.Contains("sin acceso");

    private static bool IsBatteryTip(string tip) =>
        tip.Contains("batería") || tip.Contains("bateria") || tip.Contains("battery") ||
        tip.Contains("disponible") || tip.Contains("carga") || tip.Contains("charging") ||
        tip.Contains("charged") || tip.Contains("enchufad") || tip.Contains("plugged") ||
        tip.Contains("quedan") || tip.Contains("restante") || tip.Contains("remaining") ||
        tip.Contains("energía") || tip.Contains("energia");

    private void LaunchShell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"[KairosDock] shell '{target}' failed: {ex.Message}"); }
    }

    private void ForwardTrayClick(SystemTray.TrayItem item, bool rightClick)
    {
        // Hold the panel open through the foreground-steal that the app's menu needs.
        _trayInteracting = true;
        _suppressHideUntil = DateTime.Now.AddMilliseconds(900);
        SystemTray.SendClick(item, rightClick);
    }

    // Keep the panel out of Alt-Tab (we Activate() it explicitly in Toggle()).
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOOLWINDOW);
    }
}
