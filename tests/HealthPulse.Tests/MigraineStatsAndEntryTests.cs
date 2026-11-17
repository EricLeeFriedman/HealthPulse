using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public class MigraineStatsAndEntryTests
{
    static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0);

    static MigraineEntry Attack(int daysAgo, bool triptan = false) => new()
    {
        StartTime = Now.AddDays(-daysAgo),
        EndTime = Now.AddDays(-daysAgo).AddHours(4),
        TriptanTaken = triptan,
        TriptanTime = triptan ? Now.AddDays(-daysAgo).AddMinutes(30) : null
    };

    [Fact]
    public void Last30Days_CountsAttacksDaysAndTriptanDays()
    {
        var entries = new[] { Attack(1, true), Attack(1), Attack(5, true), Attack(40, true) };
        var (attacks, migraineDays, triptanDays) = MigraineStats.Last30Days(entries, Now);
        Assert.Equal(3, attacks);
        Assert.Equal(2, migraineDays);
        Assert.Equal(2, triptanDays);
    }

    [Fact]
    public void Describe_WarnsAtTenTriptanDays()
    {
        var entries = Enumerable.Range(1, 10).Select(d => Attack(d, true)).ToList();
        Assert.Contains("10+ triptan days", MigraineStats.Describe(entries, Now));
        Assert.DoesNotContain("⚠️", MigraineStats.Describe(entries.Take(9), Now));
        Assert.Equal("Last 30 days: 1 attack on 1 day • triptan on 0 days", MigraineStats.Describe([Attack(2)], Now));
    }

    [Fact]
    public void OngoingDescription_ShowsElapsedPainAndTriptan()
    {
        var entry = new MigraineEntry
        {
            StartTime = Now.AddHours(-2.5),
            PeakSeverity = 6,
            TriptanTaken = true,
            TriptanTime = Now.AddHours(-2)
        };
        Assert.Equal("Started 9:30 AM (2.5 h ago), pain 6/10, triptan at 10:00 AM",
            MigraineStats.OngoingDescription(entry, Now));
    }

    [Fact]
    public void Entry_SummaryAndTriggerHelpers()
    {
        var entry = new MigraineEntry
        {
            StartTime = new DateTime(2026, 11, 16, 9, 0, 0),
            EndTime = new DateTime(2026, 11, 16, 12, 30, 0),
            PeakSeverity = 7,
            TriptanTaken = true,
            TriptanRelief = "Full",
            Aura = true
        };
        entry.TriggerList = [" Stress ", "", "Caffeine"];

        Assert.Equal("Stress|Caffeine", entry.Triggers);
        Assert.Equal("Stress, Caffeine", entry.TriggerSummary);
        Assert.True(entry.HasTriggers);
        Assert.Equal("Pain 7/10 • 3.5 h • triptan (full relief) • aura", entry.Summary);
        Assert.Equal("Mon Nov 16, 9:00 AM", entry.DisplayStart);

        entry.EndTime = null;
        Assert.True(entry.IsOngoing);
        Assert.Null(entry.DurationHours);
        Assert.Contains("ongoing", entry.Summary);
    }
}
