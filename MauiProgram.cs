using Microsoft.Extensions.Logging;
using HealthPulse.Services;
using HealthPulse.ViewModels;
using HealthPulse.Views;

namespace HealthPulse;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "healthpulse.db3");

        builder.Services.AddSingleton<ISettingsStore, PreferencesSettingsStore>();
        builder.Services.AddSingleton<IMigraineService>(services =>
            new LocalMigraineService(dbPath, services.GetRequiredService<ILogger<LocalMigraineService>>()));
#if ANDROID
        builder.Services.AddSingleton<IHealthDataService, Platforms.Android.HealthConnectService>();
        builder.Services.AddSingleton<IExportDestination, Platforms.Android.DocumentExportDestination>();
#else
        builder.Services.AddSingleton<IHealthDataService, UnsupportedHealthDataService>();
        builder.Services.AddSingleton<IExportDestination, UnsupportedExportDestination>();
#endif
        builder.Services.AddSingleton<HealthExportService>();

        builder.Services.AddSingleton<MigrainesViewModel>();
        builder.Services.AddTransient<MigraineEditViewModel>();
        builder.Services.AddSingleton<HealthViewModel>();
        builder.Services.AddSingleton<MigrainesPage>();
        builder.Services.AddTransient<MigraineEditPage>();
        builder.Services.AddSingleton<HealthPage>();
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
