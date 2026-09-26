using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace KairosMenu
{
    public partial class MainWindow : Window
    {
        private readonly AppIndexer _apps = new();
        private HwndSource? _source;
        private const int HotkeyId = 0x4B4D; // "KM"

        public MainWindow()
        {
            InitializeComponent();
            UsageTracker.Load();
            _apps.Build();
            Loaded += OnLoaded;
        }

        public void InitializeHidden()
        {
            Left = -10000; Top = -10000;
            Show();
            Hide();
            Visibility = Visibility.Hidden;
            Deactivated += (_, _) => HideMenu();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            _source = HwndSource.FromHwnd(helper.Handle);
            _source?.AddHook(WndProc);

            EnableAcrylic(helper.Handle);
            RegisterHotkey(helper.Handle);

            try { UserName.Text = Environment.UserName; } catch { }
        }

        // ---------- Acrylic ----------
        private void EnableAcrylic(IntPtr hwnd)
        {
            int corner = Interop.DWMWCP_ROUND;
            Interop.DwmSetWindowAttribute(hwnd, Interop.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            var accent = new Interop.AccentPolicy
            {
                AccentState = Interop.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = 0x88221414
            };
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new Interop.WindowCompositionAttributeData
            {
                Attribute = Interop.WindowCompositionAttribute.WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size
            };
            Interop.SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(ptr);
        }

        // ---------- Hotkey (Ctrl+Shift+Espacio: abre el menu) ----------
        private void RegisterHotkey(IntPtr hwnd)
        {
            // Ctrl+Shift+Espacio: combinacion libre y registrable de forma fiable.
            bool ok = Interop.RegisterHotKey(hwnd, HotkeyId,
                Interop.MOD_CONTROL | Interop.MOD_SHIFT | Interop.MOD_NOREPEAT,
                Interop.VK_SPACE);

            if (!ok)
            {
                MessageBox.Show(
                    "KairosMenu no pudo registrar el atajo Ctrl+Shift+Espacio.\n" +
                    "Puede que otra aplicacion lo este usando.",
                    "KairosMenu", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void CleanupHotkey()
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
                Interop.UnregisterHotKey(helper.Handle, HotkeyId);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Interop.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                Toggle();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // ---------- Mostrar / ocultar ----------
        private void Toggle()
        {
            if (Visibility == Visibility.Visible) HideMenu();
            else ShowMenu();
        }

        private void ShowMenu()
        {
            PositionBottomLeft();
            Search.Text = "";
            RefreshApps("");
            Visibility = Visibility.Visible;
            Activate();
            var helper = new WindowInteropHelper(this);
            Interop.SetForegroundWindow(helper.Handle);
            Search.Focus();
            Keyboard.Focus(Search);
            PlayShowAnimation();
        }

        // Animacion de entrada: fade + leve escala desde abajo (crece sobre el dock).
        private void PlayShowAnimation()
        {
            var dur = TimeSpan.FromMilliseconds(170);
            var ease = new System.Windows.Media.Animation.CubicEase
            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, dur);
            var sx = new System.Windows.Media.Animation.DoubleAnimation(0.97, 1, dur) { EasingFunction = ease };
            var sy = new System.Windows.Media.Animation.DoubleAnimation(0.97, 1, dur) { EasingFunction = ease };

            Root.BeginAnimation(OpacityProperty, fade);
            RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
            RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sy);
        }

        public void HideMenu()
        {
            Visibility = Visibility.Hidden;
        }

        // Centrado horizontal, anclado arriba del dock (estilo Win11).
        // Deja hueco inferior amplio para quedar por ENCIMA del dock, sin taparlo.
        private void PositionBottomLeft()
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Bottom - Height - 96;
        }

        // ---------- Busqueda / grid ----------
        private void RefreshApps(string query)
        {
            var list = _apps.Filter(query);
            AppsGrid.ItemsSource = list;
            SectionLabel.Text = string.IsNullOrWhiteSpace(query)
                ? "Todas las aplicaciones"
                : $"Resultados ({list.Count})";
        }

        private void Search_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RefreshApps(Search.Text);
        }

        private void Search_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HideMenu();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                // Enter lanza la primera app de la lista filtrada.
                if (AppsGrid.Items.Count > 0 && AppsGrid.Items[0] is AppEntry first)
                {
                    Launch(first);
                    e.Handled = true;
                }
            }
        }

        // ---------- Lanzar apps ----------
        private void AppButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is AppEntry app)
                Launch(app);
        }

        private void Launch(AppEntry app)
        {
            try
            {
                UsageTracker.Record(app.Path);
                Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
            }
            catch { }
            HideMenu();
        }

        // ---------- Energia ----------
        private void Lock_Click(object sender, RoutedEventArgs e)
        {
            HideMenu();
            try { Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { UseShellExecute = true }); }
            catch { }
        }

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            HideMenu();
            RunShutdown("/r /t 0");
        }

        private void Shutdown_Click(object sender, RoutedEventArgs e)
        {
            HideMenu();
            RunShutdown("/s /t 0");
        }

        private void RunShutdown(string args)
        {
            try { Process.Start(new ProcessStartInfo("shutdown.exe", args) { UseShellExecute = true, CreateNoWindow = true }); }
            catch { }
        }
    }
}
