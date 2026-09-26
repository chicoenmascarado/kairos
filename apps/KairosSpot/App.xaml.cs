using System;
using System.Windows;

namespace KairosSpot
{
    public partial class App : Application
    {
        private MainWindow? _window;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Crear la ventana una sola vez y mantenerla viva oculta.
            // Mostrar/ocultar es instantaneo asi (no recrear cada vez).
            // Se muestra y oculta una vez para forzar la creacion del handle
            // de ventana, necesaria para registrar el hotkey y el acrylic.
            _window = new MainWindow();
            _window.InitializeHidden();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _window?.CleanupHotkey();
            base.OnExit(e);
        }
    }
}
