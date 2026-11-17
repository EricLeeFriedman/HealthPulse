using HealthPulse.Services;
using Microsoft.Extensions.Logging;

namespace HealthPulse;

public partial class App : Application
{
    readonly AppShell shell;
    readonly HealthExportService exporter;
    readonly ILogger<App> logger;

    public App(AppShell shell, HealthExportService exporter, ErrorLog errors, ILogger<App> logger)
    {
        InitializeComponent();
        this.shell = shell;
        this.exporter = exporter;
        this.logger = logger;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            errors.Record("Background task", args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                errors.Record("Crash", exception);
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(shell);
        window.Activated += (_, _) => _ = AutoExportAsync();
        return window;
    }

    async Task AutoExportAsync()
    {
        try
        {
            await exporter.ExportIfDueAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Automatic export failed");
        }
    }
}
