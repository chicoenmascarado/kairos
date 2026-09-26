using System.Diagnostics;
using System.Windows;

namespace KairosDock;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single dock only: a fresh launch takes over and terminates any other
        // KairosDock already running (e.g. an older copy left over in the VM), so
        // you never end up with the old dock and the new one on screen at once.
        KillOtherInstances();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void KillOtherInstances()
    {
        try
        {
            var me = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(me.ProcessName))
            {
                if (p.Id == me.Id)
                    continue;
                try { p.Kill(); p.WaitForExit(2000); }
                catch { /* not ours to kill / already gone */ }
            }
        }
        catch { /* best-effort */ }
    }
}
