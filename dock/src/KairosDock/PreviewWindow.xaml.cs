using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using KairosDock.Interop;
using KairosDock.Services;

namespace KairosDock;

/// <summary>
/// A Windows-taskbar-style live preview: hovering a running app's dock icon shows a
/// small glass card with a real-time thumbnail of that app's window, composited by
/// the DWM (DwmRegisterThumbnail). Non-activating and click-through.
/// </summary>
public partial class PreviewWindow : Window
{
    private IntPtr _thumb;
    private IntPtr _appWindow;

    /// <summary>Raised after the user closes the app from the preview (so the dock can react).</summary>
    public event Action? AppClosed;

    public PreviewWindow()
    {
        InitializeComponent();

        // Close (X) → ask the app's window to close, then dismiss the preview.
        CloseButton.MouseEnter += (_, _) => CloseButton.Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x6B, 0x6B));
        CloseButton.MouseLeave += (_, _) => CloseButton.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        CloseButton.MouseLeftButtonUp += (_, _) =>
        {
            if (_appWindow != IntPtr.Zero)
                RunningApps.CloseWindow(_appWindow);
            HidePreview();
            AppClosed?.Invoke();
        };
    }

    /// <summary>
    /// Show a live preview of <paramref name="appWindow"/>, centred horizontally on
    /// <paramref name="centerXDip"/> with its bottom <paramref name="bottomYDip"/>
    /// (DIPs) — i.e. just above the dock.
    /// </summary>
    public void ShowFor(IntPtr appWindow, double centerXDip, double bottomYDip, string title)
    {
        if (appWindow == IntPtr.Zero)
            return;

        _appWindow = appWindow;
        TitleText.Text = title;

        Left = centerXDip - (Width / 2.0);
        Top = bottomYDip - Height;
        Clamp();

        if (!IsVisible)
            Show();

        // (Re)register the DWM thumbnail and lay it into the ThumbArea.
        var dest = new WindowInteropHelper(this).Handle;
        Unregister();
        if (DwmRegisterThumbnail(dest, appWindow, out _thumb) != 0 || _thumb == IntPtr.Zero)
            return;

        UpdateThumbnail();
    }

    /// <summary>Maps the ThumbArea element's on-screen rect (physical px) to the DWM thumbnail.</summary>
    private void UpdateThumbnail()
    {
        if (_thumb == IntPtr.Zero)
            return;

        // ThumbArea bounds relative to this window, in DIPs → physical px.
        GeneralTransform t = ThumbArea.TransformToAncestor(this);
        Rect r = t.TransformBounds(new Rect(0, 0, ThumbArea.ActualWidth, ThumbArea.ActualHeight));
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;

        int left = (int)(r.Left * dpi);
        int top = (int)(r.Top * dpi);
        int right = (int)(r.Right * dpi);
        int bottom = (int)(r.Bottom * dpi);

        // Preserve the source window's aspect ratio inside the area.
        if (DwmQueryThumbnailSourceSize(_thumb, out PSIZE src) == 0 && src.cx > 0 && src.cy > 0)
        {
            double areaW = right - left, areaH = bottom - top;
            double scale = Math.Min(areaW / src.cx, areaH / src.cy);
            double w = src.cx * scale, h = src.cy * scale;
            double offX = left + ((areaW - w) / 2.0);
            double offY = top + ((areaH - h) / 2.0);
            left = (int)offX; top = (int)offY; right = (int)(offX + w); bottom = (int)(offY + h);
        }

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT { Left = left, Top = top, Right = right, Bottom = bottom },
            opacity = 255,
            fVisible = true,
            fSourceClientAreaOnly = false,
        };
        DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    public void HidePreview()
    {
        Unregister();
        if (IsVisible)
            Hide();
    }

    private void Unregister()
    {
        if (_thumb != IntPtr.Zero)
        {
            try { DwmUnregisterThumbnail(_thumb); } catch { /* ignore */ }
            _thumb = IntPtr.Zero;
        }
    }

    private void Clamp()
    {
        var wa = SystemParameters.WorkArea;
        Left = Math.Max(wa.Left + 4, Math.Min(Left, wa.Right - Width - 4));
        if (Top < wa.Top + 4) Top = wa.Top + 4;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // No activation, no Alt-Tab — but DO accept mouse input (so the X works).
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

        // Rounded corners via a window region (the window can't be layered because
        // the DWM thumbnail won't composite into a per-pixel-alpha window).
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int w = (int)Math.Round(Width * dpi);
        int h = (int)Math.Round(Height * dpi);
        int r = (int)Math.Round(16 * dpi);
        IntPtr rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, r, r);
        SetWindowRgn(hwnd, rgn, true); // the OS owns the region now
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    // --- DWM thumbnail interop --------------------------------------------
    private const int DWM_TNP_RECTDESTINATION = 0x1;
    private const int DWM_TNP_OPACITY = 0x4;
    private const int DWM_TNP_VISIBLE = 0x8;
    private const int DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PSIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out PSIZE size);
}
