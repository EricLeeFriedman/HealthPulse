namespace HealthPulse.Services;

public static class NightlySchedule
{
    /// <summary>When the nightly export runs (local time).</summary>
    public static readonly TimeSpan ExportTime = new(23, 45, 0);

    /// <summary>The next occurrence of <paramref name="timeOfDay"/> strictly after <paramref name="now"/>.</summary>
    public static DateTime NextRun(DateTime now, TimeSpan timeOfDay)
    {
        var today = now.Date + timeOfDay;
        return today > now ? today : today.AddDays(1);
    }
}
