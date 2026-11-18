using Android.App;
using Android.Runtime;
using HealthPulse.Platforms.Android;
using HealthPulse.Services;

namespace HealthPulse
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override void OnCreate()
        {
            base.OnCreate();
            AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
                IPlatformApplication.Current?.Services.GetService<ErrorLog>()?.Record("Crash", args.Exception);
            try
            {
                NightlyExportScheduler.Schedule(this);
            }
            catch (Exception exception)
            {
                IPlatformApplication.Current?.Services.GetService<ErrorLog>()?.Record("Scheduling nightly export", exception);
            }
            try
            {
                ReminderPlatform.Schedule(this);
            }
            catch (Exception exception)
            {
                IPlatformApplication.Current?.Services.GetService<ErrorLog>()?.Record("Scheduling reminders", exception);
            }
        }
    }
}
