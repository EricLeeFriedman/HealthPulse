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
        builder.Services.AddSingleton(new ErrorLog(Path.Combine(FileSystem.AppDataDirectory, "errors.log")));
        builder.Services.AddSingleton(new GoogleDriveClient(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }));
        builder.Services.AddSingleton<IMigraineService>(services =>
            new LocalMigraineService(dbPath, services.GetRequiredService<ILogger<LocalMigraineService>>()));
#if ANDROID
        builder.Services.AddSingleton<IHealthDataService, Platforms.Android.HealthConnectService>();
        builder.Services.AddSingleton<IGoogleAuth, Platforms.Android.GoogleAuthService>();
#else
        builder.Services.AddSingleton<IHealthDataService, UnsupportedHealthDataService>();
        builder.Services.AddSingleton<IGoogleAuth, UnsupportedGoogleAuth>();
#endif
        builder.Services.AddSingleton<IExportDestination, DriveExportDestination>();
        builder.Services.AddSingleton(services =>
        {
            var errors = services.GetRequiredService<ErrorLog>();
            return new HealthExportService(
                services.GetRequiredService<IHealthDataService>(),
                services.GetRequiredService<IMigraineService>(),
                services.GetRequiredService<IExportDestination>(),
                services.GetRequiredService<ISettingsStore>(),
                services.GetRequiredService<ILogger<HealthExportService>>())
            {
                RecentErrors = () => errors.Entries,
                ExportFailed = exception => errors.Record("Export", exception)
            };
        });

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
