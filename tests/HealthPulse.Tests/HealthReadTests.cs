using System.Text.Json;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public class HealthReadReportTests
{
    [Fact]
    public async Task RunAsync_NotesSuccessAndFailureWithTiming()
    {
        var report = new HealthReadReport(TimeSpan.FromMinutes(1));

        Assert.True(await report.RunAsync("sleep", () => Task.CompletedTask));
        Assert.False(await report.RunAsync("hrv", () => throw new TimeoutException("Health Connect didn't answer")));

        Assert.StartsWith("sleep: ok in ", report.Steps[0]);
        Assert.Contains("hrv: failed after", report.Steps[1]);
        Assert.Contains("TimeoutException: Health Connect didn't answer", report.Steps[1]);
    }

    [Fact]
    public async Task RunAsync_SkipsStepsOnceBudgetIsSpent()
    {
        var report = new HealthReadReport(TimeSpan.FromMilliseconds(50));
        await report.RunAsync("slow", () => Task.Delay(100));
        var ran = false;

        Assert.True(report.OutOfTime);
        Assert.False(await report.RunAsync("weight", () => { ran = true; return Task.CompletedTask; }));
        Assert.False(ran);
        Assert.Equal("weight: skipped, out of time", report.Steps[^1]);
    }
}

public class SharedHealthDataTests
{
    static readonly DateOnly Today = new(2026, 11, 17);

    sealed class CountingHealth : IHealthDataService
    {
        public int Calls;
        public (DateOnly From, DateOnly To) LastRange;
        public TaskCompletionSource? Gate;
        public bool Fail;
        public HealthDataAvailability GetAvailability() => HealthDataAvailability.Available;
        public Task<bool> HasPermissionsAsync() => Task.FromResult(true);
        public Task<bool> RequestPermissionsAsync() => Task.FromResult(true);
        public IReadOnlyList<string> LastReadSteps => ["sleep: ok in 5 ms"];

        public async Task<List<DailyHealthSummary>> GetDailySummariesAsync(DateOnly from, DateOnly to)
        {
            Calls++;
            LastRange = (from, to);
            if (Gate != null) await Gate.Task;
            if (Fail) throw new TimeoutException("stuck");
            return HealthSummaryBuilder.CreateDays(from, to).Values.OrderBy(d => d.Date).ToList();
        }
    }

    [Fact]
    public async Task SmallReadIsWidenedToNinetyDaysAndSliced()
    {
        var inner = new CountingHealth();
        var shared = new SharedHealthData(inner);

        var days = await shared.GetDailySummariesAsync(Today.AddDays(-13), Today);

        Assert.Equal(14, days.Count);
        Assert.Equal(Today.AddDays(-89), inner.LastRange.From);
        Assert.Equal(Today, inner.LastRange.To);
    }

    [Fact]
    public async Task ConcurrentCallersShareOneRead()
    {
        var inner = new CountingHealth { Gate = new TaskCompletionSource() };
        var shared = new SharedHealthData(inner);

        var tab = shared.GetDailySummariesAsync(Today.AddDays(-13), Today);
        var fitness = shared.GetDailySummariesAsync(Today.AddDays(-13), Today);
        var export = shared.GetDailySummariesAsync(Today.AddDays(-89), Today);
        inner.Gate.SetResult();

        Assert.Equal(14, (await tab).Count);
        Assert.Equal(14, (await fitness).Count);
        Assert.Equal(90, (await export).Count);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task ResultIsReusedBrieflyThenReadAgain()
    {
        var clock = new DateTime(2026, 11, 17, 20, 0, 0, DateTimeKind.Utc);
        var inner = new CountingHealth();
        var shared = new SharedHealthData(inner, () => clock);

        await shared.GetDailySummariesAsync(Today.AddDays(-89), Today);
        clock = clock.AddMinutes(1);
        await shared.GetDailySummariesAsync(Today.AddDays(-13), Today);
        Assert.Equal(1, inner.Calls);

        clock = clock.AddMinutes(5);
        await shared.GetDailySummariesAsync(Today.AddDays(-13), Today);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task FailedReadIsNotCachedAndRequestPermissionsClearsCache()
    {
        var inner = new CountingHealth { Fail = true };
        var shared = new SharedHealthData(inner);

        await Assert.ThrowsAsync<TimeoutException>(() => shared.GetDailySummariesAsync(Today, Today));
        inner.Fail = false;
        await shared.GetDailySummariesAsync(Today, Today);
        Assert.Equal(2, inner.Calls);

        await shared.RequestPermissionsAsync();
        await shared.GetDailySummariesAsync(Today, Today);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task LongerRangeThanRunningReadWaitsThenReadsItsOwn()
    {
        var inner = new CountingHealth { Gate = new TaskCompletionSource() };
        var shared = new SharedHealthData(inner);

        var short_ = shared.GetDailySummariesAsync(Today.AddDays(-13), Today);
        var long_ = shared.GetDailySummariesAsync(Today.AddDays(-199), Today);
        inner.Gate.SetResult();

        Assert.Equal(14, (await short_).Count);
        Assert.Equal(200, (await long_).Count);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Export_IncludesHealthConnectSteps()
    {
        var destination = new MemoryDestination();
        var migraines = new LocalMigraineService(Path.Combine(Path.GetTempPath(), $"steps-{Guid.NewGuid():N}.db3"));
        var exporter = new HealthExportService(new SharedHealthData(new CountingHealth()), migraines, destination,
            new InMemorySettingsStore());

        Assert.Null(await exporter.ExportNowAsync());

        using var doc = JsonDocument.Parse(destination.Last!);
        Assert.Equal("sleep: ok in 5 ms", doc.RootElement.GetProperty("healthConnectSteps")[0].GetString());
        await migraines.Database.CloseAsync();
    }

    sealed class MemoryDestination : IExportDestination
    {
        public string? Last;
        public bool HasDestination => true;
        public string? DestinationName => "test.json";
        public Task<bool> ChooseDestinationAsync(string suggestedFileName) => Task.FromResult(true);
        public void Disconnect() { }
        public Task WriteAsync(string content) { Last = content; return Task.CompletedTask; }
    }
}
