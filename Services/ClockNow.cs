namespace HealthPulse.Services;

/// <summary>What a "Now" button next to a time picker sets: the current minute (seconds dropped).</summary>
public static class ClockNow
{
    public static TimeSpan Time(DateTime now) => new(now.Hour, now.Minute, 0);

    /// <summary>For a date + time pair: today's date and the current minute.</summary>
    public static (DateTime Date, TimeSpan Clock) Stamp(DateTime now) => (now.Date, Time(now));
}
