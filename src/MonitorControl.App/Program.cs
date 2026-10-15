using MonitorControl.App.Platform;
using MonitorControl.App.Services;
using Velopack;

namespace MonitorControl.App;

/// <summary>Entry point.</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: handles install/update/uninstall hooks of the Velopack installer.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.Set(false))
            .Run();

        if (args.Contains("--diagnose", StringComparer.OrdinalIgnoreCase))
        {
            return DiagnosticReport.Run(open: !args.Contains("--no-open", StringComparer.OrdinalIgnoreCase));
        }

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            return 0;
        }

        var app = new App();
        app.InitializeComponent();
        instance.Listen(() => app.Dispatcher.BeginInvoke(app.OpenSettings));
        return app.Run();
    }
}
