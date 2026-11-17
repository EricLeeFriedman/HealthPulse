using HealthPulse.Services;

namespace HealthPulse.Tests;

public class ScheduleAndErrorLogTests
{
    [Theory]
    [InlineData("2026-11-17 18:00", "2026-11-17 23:45")]
    [InlineData("2026-11-17 23:44", "2026-11-17 23:45")]
    [InlineData("2026-11-17 23:45", "2026-11-18 23:45")]
    [InlineData("2026-11-17 23:59", "2026-11-18 23:45")]
    [InlineData("2026-12-31 23:50", "2027-01-01 23:45")]
    public void NextRun_IsTheNext1145PM(string now, string expected)
    {
        var next = NightlySchedule.NextRun(DateTime.Parse(now), NightlySchedule.ExportTime);
        Assert.Equal(DateTime.Parse(expected), next);
    }

    [Fact]
    public void ErrorLog_KeepsNewestFirstAndCapsEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"errors-{Guid.NewGuid():N}", "errors.log");
        var log = new ErrorLog(path);
        Assert.Empty(log.Entries);

        for (var i = 0; i < ErrorLog.MaxEntries + 3; i++)
            log.Record("Test", new InvalidOperationException($"problem {i}"), new DateTime(2026, 11, 17, 10, 0, i));

        var entries = log.Entries;
        Assert.Equal(ErrorLog.MaxEntries, entries.Count);
        Assert.Contains($"problem {ErrorLog.MaxEntries + 2}", entries[0]);
        Assert.StartsWith("2026-11-17 10:00:12 Test: System.InvalidOperationException", entries[0]);

        // Survives a restart (new instance on the same file).
        Assert.Equal(ErrorLog.MaxEntries, new ErrorLog(path).Entries.Count);

        log.Clear();
        Assert.Empty(log.Entries);
    }
}
