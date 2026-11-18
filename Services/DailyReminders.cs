using System.Globalization;
using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>One thing still to do today.</summary>
public record ReminderTask(string Key, string Text);

/// <summary>
/// Reminders at 9 AM, noon, 3, 6 and 9 PM listing what's still open today:
/// preventives, the planned workout, the check-in, protein and the weekly
/// waist/weight. They stop for the day once everything is done, or when the
/// user taps "Stop for today".
/// </summary>
public static class DailyReminders
{
    public static readonly TimeSpan[] Times =
        [new(9, 0, 0), new(12, 0, 0), new(15, 0, 0), new(18, 0, 0), new(21, 0, 0)];

    /// <summary>A reminder that fires late (phone asleep) still counts until this time.</summary>
    public static readonly TimeSpan LastShowTime = new(22, 0, 0);

    public const string EnabledKey = "reminders_enabled";
    public const string StoppedDateKey = "reminders_stopped_date";

    public static bool IsEnabled(ISettingsStore settings) => settings.GetString(EnabledKey) != "false";
    public static void SetEnabled(ISettingsStore settings, bool enabled) =>
        settings.SetString(EnabledKey, enabled ? null : "false");

    public static bool IsStopped(ISettingsStore settings, DateOnly date) =>
        settings.GetString(StoppedDateKey) == DailyCheckIn.Key(date);
    public static void StopFor(ISettingsStore settings, DateOnly date) =>
        settings.SetString(StoppedDateKey, DailyCheckIn.Key(date));
    public static void Resume(ISettingsStore settings) => settings.SetString(StoppedDateKey, null);

    /// <summary>The next reminder time strictly after <paramref name="now"/>.</summary>
    public static DateTime NextTime(DateTime now)
    {
        foreach (var time in Times)
        {
            var candidate = now.Date + time;
            if (candidate > now) return candidate;
        }
        return now.Date.AddDays(1) + Times[0];
    }

    /// <summary>What's still open for <paramref name="date"/>.</summary>
    public static List<ReminderTask> OpenTasks(DateOnly date, DailyCheckIn? checkIn,
        IEnumerable<WorkoutSession> workouts, DateOnly? lastMeasured)
    {
        var tasks = new List<ReminderTask>();
        var logged = checkIn != null && checkIn.UpdatedAt != DateTime.MinValue;

        if (checkIn == null || !checkIn.AllSupplementsTaken)
        {
            var missing = new List<string>();
            if (checkIn?.Riboflavin != true) missing.Add("riboflavin");
            if (checkIn?.BComplex != true) missing.Add("B complex");
            if (checkIn?.CoQ10 != true) missing.Add("CoQ10");
            if (checkIn?.Magnesium != true) missing.Add("magnesium");
            tasks.Add(new("preventives", $"Take preventives ({string.Join(", ", missing)})"));
        }

        var planned = WorkoutPlan.PlannedFor(date.DayOfWeek);
        if (planned != null && !WorkoutDone(planned, date, checkIn, workouts))
            tasks.Add(new("workout", planned == WorkoutPlan.Mobility ? "Do your mobility routine" : $"Do today's workout: {planned}"));

        if (!logged)
            tasks.Add(new("checkin", "Log today's check-in (caffeine, water, stress)"));

        if (checkIn?.ProteinGrams is not { } protein || protein < TrackerRules.ProteinMinGrams)
        {
            var have = checkIn?.ProteinGrams is { } grams ? $" ({grams} g so far)" : "";
            tasks.Add(new("protein", $"Reach {TrackerRules.ProteinMinGrams} g protein{have}"));
        }

        if (TrackerRules.IsWeeklyMeasurementDue(lastMeasured, date))
            tasks.Add(new("measure", "Log weekly waist and weight"));

        return tasks;
    }

    static bool WorkoutDone(string planned, DateOnly date, DailyCheckIn? checkIn, IEnumerable<WorkoutSession> workouts)
    {
        var today = workouts.Where(w => !w.Hidden && DateOnly.FromDateTime(w.Date) == date).ToList();
        if (planned == WorkoutPlan.Mobility)
            return checkIn?.MobilityDone == true || today.Any(w => w.SessionType == WorkoutPlan.Mobility);
        if (WorkoutPlan.IsStrength(planned))
            return today.Any(w => WorkoutPlan.IsStrength(w.SessionType));
        return today.Any(w => w.SessionType == planned);
    }

    /// <summary>Whether to show a reminder now.</summary>
    public static bool ShouldNotify(DateTime now, ISettingsStore settings, IReadOnlyCollection<ReminderTask> tasks) =>
        IsEnabled(settings)
        && !IsStopped(settings, DateOnly.FromDateTime(now))
        && now.TimeOfDay >= Times[0] && now.TimeOfDay < LastShowTime
        && tasks.Count > 0;

    public static string Title(IReadOnlyCollection<ReminderTask> tasks) =>
        tasks.Count == 1 ? "1 thing left today" : $"{tasks.Count} things left today";

    public static string Body(IReadOnlyCollection<ReminderTask> tasks) =>
        string.Join("\n", tasks.Select(t => "• " + t.Text));

    /// <summary>One line for the Today tab.</summary>
    public static string StatusText(DateTime now, ISettingsStore settings, IReadOnlyCollection<ReminderTask> tasks)
    {
        if (!IsEnabled(settings)) return "Reminders are off.";
        var next = NextTime(now);
        var nextText = next.ToString("h tt", CultureInfo.InvariantCulture);
        if (tasks.Count == 0) return "Everything's done for today. No more reminders until tomorrow at 9 AM.";
        if (IsStopped(settings, DateOnly.FromDateTime(now))) return "Reminders are stopped for today. They start again tomorrow at 9 AM.";
        return next.Date == now.Date
            ? $"Next reminder at {nextText} for {tasks.Count} open item{(tasks.Count == 1 ? "" : "s")}."
            : "Next reminder tomorrow at 9 AM.";
    }

    public static async Task<List<ReminderTask>> LoadOpenTasksAsync(ITrackerStore store, DateOnly date)
    {
        var checkIn = await store.GetCheckInAsync(date);
        var workouts = await store.GetWorkoutsAsync(date, date);
        var measurements = await store.GetMeasurementsAsync(date.AddDays(-7), date);
        DateOnly? lastMeasured = measurements.Count > 0
            ? DateOnly.FromDateTime(measurements.Max(m => m.Date))
            : null;
        return OpenTasks(date, checkIn, workouts, lastMeasured);
    }
}

/// <summary>The phone side of reminders: scheduling and clearing the notification.</summary>
public interface IReminderPlatform
{
    void Reschedule();
    void ClearShown();
}

public class UnsupportedReminderPlatform : IReminderPlatform
{
    public void Reschedule() { }
    public void ClearShown() { }
}
