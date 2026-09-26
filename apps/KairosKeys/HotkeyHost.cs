using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;

namespace KairosKeys
{
    // Aloja una ventana-mensaje invisible que recibe los WM_HOTKEY del sistema.
    // Registra el set de atajos de Kairos y ejecuta la accion correspondiente.
    public class HotkeyHost
    {
        private HwndSource? _source;
        private IntPtr _hwnd;
        private readonly Dictionary<int, Action> _actions = new();
        private int _nextId = 1;

        // Definicion de un atajo: modificadores + tecla + accion.
        private record Hotkey(uint Mods, uint Vk, Action Do, string Label);

        public void Start()
        {
            // Crear una ventana-mensaje invisible (HWND_MESSAGE).
            var parameters = new HwndSourceParameters("KairosKeysHost")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3) // HWND_MESSAGE
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            _hwnd = _source.Handle;

            RegisterAll();
        }

        private void RegisterAll()
        {
            // Win+Alt: apps can't use the Windows key, so these never clash with a
            // DAW's shortcuts, and AltGr (= Ctrl+Alt on Spanish keyboards: €, @, #…)
            // keeps working. Up/Down are taken by Windows 11's snap, hence PgUp/PgDn.
            uint CA = Interop.MOD_WIN | Interop.MOD_ALT | Interop.MOD_NOREPEAT;

            var set = new List<Hotkey>
            {
                new(CA, Interop.VK_E,    Actions.OpenKairosFiles, "Abrir explorador"),
                new(CA, Interop.VK_S,    Actions.SnipRegion,      "Captura de region"),
                new(CA, Interop.VK_D,    Actions.ShowDesktop,     "Mostrar escritorio"),
                new(CA, Interop.VK_L,    Actions.LockScreen,      "Bloquear pantalla"),
                new(CA, Interop.VK_V,    Actions.PastePlainText,  "Pegar texto plano"),
                new(CA, Interop.VK_PRIOR, Actions.VolumeUp,        "Subir volumen"),
                new(CA, Interop.VK_NEXT,  Actions.VolumeDown,      "Bajar volumen"),
                new(CA, Interop.VK_M,    Actions.VolumeMute,      "Silenciar"),
            };

            var failed = new List<string>();

            foreach (var hk in set)
            {
                int id = _nextId++;
                bool ok = Interop.RegisterHotKey(_hwnd, id, hk.Mods, hk.Vk);
                if (ok) _actions[id] = hk.Do;
                else failed.Add(hk.Label);
            }

            if (failed.Count > 0)
            {
                MessageBox.Show(
                    "Estos atajos no se pudieron registrar (otra app los usa):\n- " +
                    string.Join("\n- ", failed),
                    "KairosKeys", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Interop.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_actions.TryGetValue(id, out var action))
                {
                    try { action(); } catch { }
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Stop()
        {
            foreach (var id in _actions.Keys)
                Interop.UnregisterHotKey(_hwnd, id);
            _actions.Clear();
            _source?.Dispose();
        }
    }
}
