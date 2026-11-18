using Android.App;
using Android.App.Job;
using Android.Content;
using HealthPulse.Services;
using Microsoft.Extensions.Logging;

namespace HealthPulse.Platforms.Android;

/// <summary>
/// Runs the Drive export every night at 11:45 PM: an exact alarm wakes the
/// app, which hands the work to an expedited JobScheduler job.
/// </summary>
public static class NightlyExportScheduler
{
    const int AlarmRequestCode = 2345;
    public const int JobId = 2346;

    public static DateTime Schedule(Context context)
    {
        var next = NightlySchedule.NextRun(DateTime.Now, NightlySchedule.ExportTime);
        var alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        var intent = new Intent(context, typeof(NightlyExportReceiver)).SetAction(NightlyExportReceiver.ActionExport);
        var pending = PendingIntent.GetBroadcast(context, AlarmRequestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        var triggerAt = new DateTimeOffset(next).ToUnixTimeMilliseconds();

        if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarmManager.CanScheduleExactAlarms())
            alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, pending);
        else
            alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, pending);
        return next;
    }

    public static void StartJobNow(Context context)
    {
        var scheduler = (JobScheduler)context.GetSystemService(Context.JobSchedulerService)!;
        var builder = new JobInfo.Builder(JobId, new ComponentName(context, Java.Lang.Class.FromType(typeof(NightlyExportJobService))))
            .SetRequiredNetworkType(NetworkType.Any);
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            builder.SetExpedited(true);
        else
            builder.SetOverrideDeadline(0);
        scheduler.Schedule(builder.Build()!);
    }
}

[BroadcastReceiver(Name = "com.ericfriedman.healthpulse.NightlyExportReceiver", Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, "android.intent.action.MY_PACKAGE_REPLACED",
    "android.app.action.SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED"])]
public class NightlyExportReceiver : BroadcastReceiver
{
    public const string ActionExport = "com.ericfriedman.healthpulse.NIGHTLY_EXPORT";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null) return;
        if (intent?.Action == ActionExport)
            NightlyExportScheduler.StartJobNow(context);
        // Always line up the next night (also after reboot or an app update).
        NightlyExportScheduler.Schedule(context);
        if (intent?.Action != ActionExport)
            ReminderPlatform.Schedule(context);
    }
}

[Service(Name = "com.ericfriedman.healthpulse.NightlyExportJobService",
    Permission = "android.permission.BIND_JOB_SERVICE", Exported = true)]
public class NightlyExportJobService : JobService
{
    public override bool OnStartJob(JobParameters? parameters)
    {
        _ = RunAsync(parameters);
        return true;
    }

    async Task RunAsync(JobParameters? parameters)
    {
        var services = IPlatformApplication.Current?.Services;
        try
        {
            var exporter = services?.GetService<HealthExportService>();
            if (exporter != null)
                await exporter.ExportIfDueAsync(force: true);
        }
        catch (Exception exception)
        {
            services?.GetService<ErrorLog>()?.Record("Nightly export", exception);
            services?.GetService<ILogger<NightlyExportJobService>>()?.LogError(exception, "Nightly export failed");
        }
        finally
        {
            JobFinished(parameters, false);
        }
    }

    public override bool OnStopJob(JobParameters? parameters) => false;
}
