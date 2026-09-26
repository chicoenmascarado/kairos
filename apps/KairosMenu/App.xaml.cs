using System.Windows;

namespace KairosMenu
{
    public partial class App : Application
    {
        private MainWindow? _window;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
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
