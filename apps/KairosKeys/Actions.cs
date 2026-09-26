using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;

namespace KairosKeys
{
    // Implementa lo que hace cada atajo.
    public static class Actions
    {
        // Abrir el explorador propio KairosFiles (instalado en LOCALAPPDATA).
        public static void OpenKairosFiles()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Kairos", "KairosFiles", "KairosFiles.exe");

            try
            {
                if (File.Exists(path))
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                else
                    // Respaldo: explorador de Windows si KairosFiles no esta instalado.
                    Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }
            catch { }
        }

        // Captura de region a portapapeles (usa la herramienta de recortes de Windows).
        public static void SnipRegion()
        {
            try { Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true }); }
            catch { }
        }

        // Mostrar escritorio (minimizar todo). Usa el shell de Windows.
        public static void ShowDesktop()
        {
            try
            {
                dynamic? shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                shell?.ToggleDesktop();
            }
            catch { }
        }

        // Bloquear la sesion.
        public static void LockScreen()
        {
            try { Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { UseShellExecute = true }); }
            catch { }
        }

        // Volumen
        public static void VolumeUp()   => Tap(Interop.VK_VOLUME_UP);
        public static void VolumeDown() => Tap(Interop.VK_VOLUME_DOWN);
        public static void VolumeMute() => Tap(Interop.VK_VOLUME_MUTE);

        private static void Tap(byte vk)
        {
            Interop.keybd_event(vk, 0, 0, UIntPtr.Zero);
            Interop.keybd_event(vk, 0, Interop.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // Pegar como texto plano: toma el texto del portapapeles, lo deja como
        // texto sin formato y simula Ctrl+V.
        public static void PastePlainText()
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                string text = Clipboard.GetText();
                Clipboard.SetText(text); // re-set como texto plano (sin formato enriquecido)

                // Soltar Win y Alt (siguen pulsados por el atajo); si no, la app
                // recibiria Win+Alt+Ctrl+V en vez de Ctrl+V.
                const byte VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_MENU = 0x12;
                Interop.keybd_event(VK_LWIN, 0, Interop.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Interop.keybd_event(VK_RWIN, 0, Interop.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Interop.keybd_event(VK_MENU, 0, Interop.KEYEVENTF_KEYUP, UIntPtr.Zero);

                // Simular Ctrl+V
                const byte VK_CONTROL = 0x11;
                const byte VK_V = 0x56;
                Interop.keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                Interop.keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                Interop.keybd_event(VK_V, 0, Interop.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Interop.keybd_event(VK_CONTROL, 0, Interop.KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch { }
        }
    }
}
