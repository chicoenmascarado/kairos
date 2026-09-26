using System.Windows;

namespace KairosKeys
{
    public partial class App : Application
    {
        private HotkeyHost? _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _host = new HotkeyHost();
            _host.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Stop();
            base.OnExit(e);
        }
    }
}
