namespace HealthPulse.Services;

/// <summary>Splits a Health Connect read into small windows so one slow call only loses a few days.</summary>
public static class HealthReadPlan
{
    /// <summary>Windows of up to <paramref name="days"/> days covering [start, end), newest first.</summary>
    public static IEnumerable<(DateTime Start, DateTime End)> Chunks(DateTime start, DateTime end, int days)
    {
        if (days < 1) throw new ArgumentOutOfRangeException(nameof(days));
        for (var chunkEnd = end; chunkEnd > start; chunkEnd = chunkEnd.AddDays(-days))
        {
            var chunkStart = chunkEnd.AddDays(-days);
            yield return (chunkStart < start ? start : chunkStart, chunkEnd);
        }
    }
}
