using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace KairosSpot
{
    public partial class MainWindow : Window
    {
        private readonly SearchEngine _engine = new();
        private HwndSource? _source;

        public MainWindow()
        {
            InitializeComponent();
            _engine.Initialize();
            Loaded += OnLoaded;
        }

        // Fuerza la creacion del handle (para hotkey+acrylic) sin dejar la
        // ventana visible. Se llama una vez al arrancar la app.
        public void InitializeHidden()
        {
            // Mostrar fuera de pantalla para disparar Loaded, luego ocultar.
            Left = -10000; Top = -10000;
            Show();
            Hide();
            Visibility = Visibility.Hidden;
            // Suscribir Deactivated DESPUES del arranque para no auto-ocultar
            // durante la inicializacion.
            Deactivated += (_, _) => HideToTray();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            _source = HwndSource.FromHwnd(helper.Handle);
            _source?.AddHook(WndProc);

            EnableAcrylic(helper.Handle);
            RegisterHotkey(helper.Handle);
        }

        // ---------- Acrylic glass ----------
        // Usamos el acrylic "legacy" (SetWindowCompositionAttribute) como metodo
        // principal: funciona con AllowsTransparency=True, da blur real y NO
        // depende de que Windows este activado ni de la pantalla de Configuracion.
        private void EnableAcrylic(IntPtr hwnd)
        {
            // Esquinas redondeadas del sistema (no molesta si no aplica).
            int corner = Interop.DWMWCP_ROUND;
            Interop.DwmSetWindowAttribute(hwnd, Interop.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            var accent = new Interop.AccentPolicy
            {
                AccentState = Interop.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2, // habilita el tinte en los 4 bordes
                // ABGR: tinte oscuro Kairos. A=opacidad del tinte sobre el blur.
                // 0x88 = ~53% deja ver bien el blur; 14 14 22 = #221414 (oscuro violaceo)
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

        // ---------- Hotkey global Alt+Space ----------
        private const int HotkeyId = 0x4B41; // "KA"

        private void RegisterHotkey(IntPtr hwnd)
        {
            Interop.RegisterHotKey(hwnd, HotkeyId,
                Interop.MOD_ALT | Interop.MOD_NOREPEAT, Interop.VK_SPACE);
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
            if (Visibility == Visibility.Visible)
                HideToTray();
            else
                ShowCentered();
        }

        private void ShowCentered()
        {
            PositionTopCenter();
            Input.Text = "";
            RunQuery("");
            Visibility = Visibility.Visible;
            Activate();
            var helper = new WindowInteropHelper(this);
            Interop.SetForegroundWindow(helper.Handle);
            Input.Focus();
            System.Windows.Input.Keyboard.Focus(Input);
            PlayShowAnimation();
        }

        // Animacion de entrada: fade + leve escala (premium, estilo Spotlight).
        private void PlayShowAnimation()
        {
            var dur = TimeSpan.FromMilliseconds(160);
            var ease = new System.Windows.Media.Animation.CubicEase
            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, dur);
            var scaleFrom = 0.96;
            var sx = new System.Windows.Media.Animation.DoubleAnimation(scaleFrom, 1, dur) { EasingFunction = ease };
            var sy = new System.Windows.Media.Animation.DoubleAnimation(scaleFrom, 1, dur) { EasingFunction = ease };

            Root.BeginAnimation(OpacityProperty, fade);
            RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
            RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sy);
        }

        public void HideToTray()
        {
            Visibility = Visibility.Hidden;
        }

        // Centrado horizontal, a un tercio de altura (estilo Spotlight).
        private void PositionTopCenter()
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + wa.Height * 0.22;
        }

        // ---------- Busqueda ----------
        private void Input_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RunQuery(Input.Text);
        }

        private void RunQuery(string text)
        {
            var items = _engine.Query(text);
            Results.ItemsSource = items;
            if (Results.Items.Count > 0)
                Results.SelectedIndex = 0;

            Divider.Visibility = Results.Items.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------- Navegacion por teclado ----------
        private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    HideToTray();
                    e.Handled = true;
                    break;
                case Key.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    ExecuteSelected();
                    e.Handled = true;
                    break;
            }
        }

        private void MoveSelection(int delta)
        {
            int count = Results.Items.Count;
            if (count == 0) return;
            int idx = Results.SelectedIndex + delta;
            if (idx < 0) idx = count - 1;
            if (idx >= count) idx = 0;
            Results.SelectedIndex = idx;
            Results.ScrollIntoView(Results.SelectedItem);
        }

        private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ExecuteSelected();
        }

        private void ExecuteSelected()
        {
            if (Results.SelectedItem is not SearchResult r) return;
            Execute(r);
        }

        private void Execute(SearchResult r)
        {
            // Registrar uso (apps y archivos) para el ranking por frecuencia.
            if (r.Kind == ResultKind.App || r.Kind == ResultKind.File)
                UsageTracker.Record(r.Target);

            try
            {
                switch (r.Kind)
                {
                    case ResultKind.Calc:
                        // Copiar el resultado al portapapeles
                        try { Clipboard.SetText(r.Target); } catch { }
                        break;

                    case ResultKind.Web:
                    case ResultKind.Setting:
                        if (r.Target.EndsWith(".exe"))
                            Process.Start(new ProcessStartInfo(r.Target) { UseShellExecute = true });
                        else
                            Process.Start(new ProcessStartInfo(r.Target) { UseShellExecute = true });
                        break;

                    case ResultKind.App:
                    case ResultKind.File:
                    default:
                        Process.Start(new ProcessStartInfo(r.Target) { UseShellExecute = true });
                        break;
                }
            }
            catch
            {
                // Silencioso: si falla el lanzamiento no rompemos la app
            }

            if (r.Kind != ResultKind.Calc)
                HideToTray();
        }
    }
}
