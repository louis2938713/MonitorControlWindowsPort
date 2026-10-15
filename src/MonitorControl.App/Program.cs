namespace MonitorControl.App;

/// <summary>Entry point.</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--diagnose", StringComparer.OrdinalIgnoreCase))
        {
            return Services.DiagnosticReport.Run(open: !args.Contains("--no-open", StringComparer.OrdinalIgnoreCase));
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
