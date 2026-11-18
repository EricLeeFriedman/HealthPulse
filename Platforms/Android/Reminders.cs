using Android.App;
using Android.Content;
using HealthPulse.Services;
using Microsoft.Extensions.Logging;

namespace HealthPulse.Platforms.Android;

/// <summary>Schedules the 9/12/3/6/9 reminder alarms and shows the notification.</summary>
public class ReminderPlatform : IReminderPlatform
{
    const int AlarmRequestCode = 3345;
    const int StopRequestCode = 3346;
    const int OpenRequestCode = 3347;
    public const int NotificationId = 3348;
    public const string ChannelId = "daily_reminders";

    static Context AppContext => global::Android.App.Application.Context;

    public void Reschedule() => Schedule(AppContext);
    public void ClearShown() => Clear(AppContext);

    public static DateTime Schedule(Context context)
    {
        var next = DailyReminders.NextTime(DateTime.Now);
        var alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        var intent = new Intent(context, typeof(ReminderReceiver)).SetAction(ReminderReceiver.ActionRemind);
        var pending = PendingIntent.GetBroadcast(context, AlarmRequestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        var triggerAt = new DateTimeOffset(next).ToUnixTimeMilliseconds();
        if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarmManager.CanScheduleExactAlarms())
            alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, pending);
        else
            alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, pending);
        return next;
    }

    public static void Clear(Context context) =>
        ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Cancel(NotificationId);

    public static void Show(Context context, IReadOnlyCollection<ReminderTask> tasks)
    {
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && manager.GetNotificationChannel(ChannelId) == null)
        {
            manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Daily reminders",
                NotificationImportance.Default)
            {
                Description = "Reminders at 9, 12, 3, 6 and 9 for what's still open today"
            });
        }

        var open = PendingIntent.GetActivity(context, OpenRequestCode,
            new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var stop = PendingIntent.GetBroadcast(context, StopRequestCode,
            new Intent(context, typeof(ReminderReceiver)).SetAction(ReminderReceiver.ActionStopToday),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var body = DailyReminders.Body(tasks);

#pragma warning disable CA1416, CA1422
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(context, ChannelId)
            : new Notification.Builder(context);
        builder.SetSmallIcon(global::Android.Resource.Drawable.IcPopupReminder)
            .SetContentTitle(DailyReminders.Title(tasks))
            .SetContentText(tasks.First().Text)
            .SetStyle(new Notification.BigTextStyle().BigText(body))
            .SetContentIntent(open)
            .SetAutoCancel(true)
            .AddAction(new Notification.Action.Builder((global::Android.Graphics.Drawables.Icon?)null, "Stop for today", stop).Build());
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            builder.SetTimeoutAfter((long)TimeSpan.FromHours(3).TotalMilliseconds);
#pragma warning restore CA1416, CA1422
        manager.Notify(NotificationId, builder.Build());
    }
}

[BroadcastReceiver(Name = "com.ericfriedman.healthpulse.ReminderReceiver", Exported = false)]
public class ReminderReceiver : BroadcastReceiver
{
    public const string ActionRemind = "com.ericfriedman.healthpulse.REMIND";
    public const string ActionStopToday = "com.ericfriedman.healthpulse.REMINDERS_STOP_TODAY";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null) return;
        var pending = GoAsync();
        _ = HandleAsync(context, intent?.Action, pending);
    }

    static async Task HandleAsync(Context context, string? action, PendingResult? pending)
    {
        var services = IPlatformApplication.Current?.Services;
        try
        {
            var settings = services?.GetService<ISettingsStore>();
            var store = services?.GetService<ITrackerStore>();
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (action == ActionStopToday)
            {
                if (settings != null) DailyReminders.StopFor(settings, today);
                ReminderPlatform.Clear(context);
            }
            else if (action == ActionRemind && settings != null && store != null)
            {
                var tasks = await DailyReminders.LoadOpenTasksAsync(store, today);
                if (DailyReminders.ShouldNotify(DateTime.Now, settings, tasks))
                    ReminderPlatform.Show(context, tasks);
                else if (tasks.Count == 0)
                    ReminderPlatform.Clear(context);
            }
        }
        catch (Exception exception)
        {
            services?.GetService<ErrorLog>()?.Record("Reminder", exception);
            services?.GetService<ILogger<ReminderReceiver>>()?.LogError(exception, "Reminder failed");
        }
        finally
        {
            try { ReminderPlatform.Schedule(context); } catch { /* next app start reschedules */ }
            pending?.Finish();
        }
    }
}
