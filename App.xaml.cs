using Microsoft.UI.Xaml;

namespace EdgeDashboard;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Keep the dashboard alive on a bad sensor read; log for later diagnosis.
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "error.log"), $"{DateTime.Now:s} {e.Exception}\n\n"); } catch { }
            e.Handled = true;
        };
    }

    private static Mutex? _single;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? commandLine, int flags);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Only one dashboard at a time: a second launch (say, start-up plus a manual click) just exits.
        _single = new Mutex(true, @"Local\EdgeDashboard.SingleInstance", out bool first);
        if (!first) { Exit(); return; }

        // Ask Windows to start the dashboard again by itself if it ever crashes or hangs.
        RegisterApplicationRestart(null, 0);

        _window = new MainWindow();
        _window.Activate();
    }
}
