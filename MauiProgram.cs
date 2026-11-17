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
        builder.Services.AddSingleton<ITrackerStore>(services =>
            new LocalTrackerStore(dbPath, services.GetRequiredService<ILogger<LocalTrackerStore>>()));
#if ANDROID
        builder.Services.AddSingleton<IHealthDataService, Platforms.Android.HealthConnectService>();
        builder.Services.AddSingleton<IGoogleAuth, Platforms.Android.GoogleAuthService>();
        builder.Services.AddSingleton<ILocationProvider, Platforms.Android.AndroidLocationProvider>();
#else
        builder.Services.AddSingleton<IHealthDataService, UnsupportedHealthDataService>();
        builder.Services.AddSingleton<IGoogleAuth, UnsupportedGoogleAuth>();
        builder.Services.AddSingleton<ILocationProvider, UnsupportedLocationProvider>();
#endif
        builder.Services.AddSingleton(services => new WeatherService(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            services.GetRequiredService<ITrackerStore>(),
            services.GetRequiredService<ISettingsStore>(),
            services.GetRequiredService<ILocationProvider>(),
            services.GetRequiredService<ILogger<WeatherService>>()));
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
                ExportFailed = exception => errors.Record("Export", exception),
                Trackers = services.GetRequiredService<ITrackerStore>(),
                RefreshWeather = (today, days) =>
                    services.GetRequiredService<WeatherService>().RefreshAsync(today, days, interactive: false)
            };
        });

        builder.Services.AddSingleton<MigrainesViewModel>();
        builder.Services.AddTransient<MigraineEditViewModel>();
        builder.Services.AddSingleton<HealthViewModel>();
        builder.Services.AddSingleton<TodayViewModel>();
        builder.Services.AddSingleton<FitnessViewModel>();
        builder.Services.AddTransient<WorkoutEditViewModel>();
        builder.Services.AddTransient<MeasureViewModel>();
        builder.Services.AddSingleton<TodayPage>();
        builder.Services.AddSingleton<FitnessPage>();
        builder.Services.AddTransient<WorkoutEditPage>();
        builder.Services.AddTransient<MeasurePage>();
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
