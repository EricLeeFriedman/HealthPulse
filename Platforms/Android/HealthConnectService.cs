using Android.Runtime;
using AndroidX.Health.Connect.Client;
using AndroidX.Health.Connect.Client.Aggregate;
using AndroidX.Health.Connect.Client.Contracts;
using AndroidX.Health.Connect.Client.Records;
using AndroidX.Health.Connect.Client.Records.Metadata;
using AndroidX.Health.Connect.Client.Request;
using AndroidX.Health.Connect.Client.Response;
using AndroidX.Health.Connect.Client.Time;
using HealthPulse.Models;
using HealthPulse.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthPulse.Platforms.Android;

public class HealthConnectService : IHealthDataService
{
    public const string ReadSteps = "android.permission.health.READ_STEPS";
    public const string ReadSleep = "android.permission.health.READ_SLEEP";
    public const string ReadHeartRate = "android.permission.health.READ_HEART_RATE";
    public const string ReadRestingHeartRate = "android.permission.health.READ_RESTING_HEART_RATE";
    public const string ReadHrv = "android.permission.health.READ_HEART_RATE_VARIABILITY";
    public const string ReadExercise = "android.permission.health.READ_EXERCISE";
    public const string ReadWeight = "android.permission.health.READ_WEIGHT";
    public const string ReadDistance = "android.permission.health.READ_DISTANCE";

    /// <summary>Lets the 11:45 PM export read data while the app isn't open (Android 14+).</summary>
    public const string ReadInBackground = "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND";

    public static readonly string[] Permissions =
        [ReadSteps, ReadSleep, ReadHeartRate, ReadRestingHeartRate, ReadHrv, ReadExercise, ReadDistance, ReadWeight, ReadInBackground];

    public ILogger<HealthConnectService> Logger;

    public HealthConnectService(ILogger<HealthConnectService>? logger = null)
    {
        Logger = logger ?? NullLogger<HealthConnectService>.Instance;
    }

    static global::Android.Content.Context Context => global::Android.App.Application.Context;

    IHealthConnectClient Client => IHealthConnectClient.GetOrCreate(Context);

    public HealthDataAvailability GetAvailability()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return HealthDataAvailability.NotSupported;
        var status = IHealthConnectClient.GetSdkStatus(Context);
        return status == HealthConnectClient.SdkAvailable
            ? HealthDataAvailability.Available
            : status == 2 // SDK_UNAVAILABLE_PROVIDER_UPDATE_REQUIRED
                ? HealthDataAvailability.NeedsInstallOrUpdate
                : HealthDataAvailability.NotSupported;
    }

    public async Task<HashSet<string>> GetGrantedPermissionsAsync()
    {
        var result = await KotlinTask.AwaitAsync(c => Client.PermissionController.GetGrantedPermissions(c));
        var granted = new HashSet<string>();
        if (result == null) return granted;
        var set = result.JavaCast<Java.Util.ISet>();
        foreach (var item in set.ToArray())
        {
            var name = item?.ToString();
            if (name != null) granted.Add(name);
        }
        return granted;
    }

    public async Task<bool> HasPermissionsAsync()
    {
        if (GetAvailability() != HealthDataAvailability.Available) return false;
        var granted = await GetGrantedPermissionsAsync();
        // Sleep and steps are the minimum for a useful export.
        return granted.Contains(ReadSleep) || granted.Contains(ReadSteps);
    }

    public async Task<bool> RequestPermissionsAsync()
    {
        if (GetAvailability() != HealthDataAvailability.Available) return false;
        var route = HealthPermissionRequest.ChooseRoute((int)global::Android.OS.Build.VERSION.SdkInt);
        if (route == HealthPermissionRequest.Route.PlatformRuntimePermissions)
        {
            // Android 14+: Health Connect permissions are platform runtime permissions.
            var (names, results) = await ActivityResultBridge.RequestPermissionsAsync(Permissions);
            var granted = HealthPermissionRequest.GrantedFromResults(names, results);
            Logger.LogInformation("Health Connect permission dialog finished; granted {Count}", granted.Count);
        }
        else
        {
            // Android 13 and below: the contract's intent opens the Health Connect app.
            var contract = new HealthPermissionsRequestContract();
            var intent = contract.CreateIntentImpl(Context, new HashSet<string>(Permissions));
            var (resultCode, data) = await ActivityResultBridge.StartAsync(intent);
            var granted = contract.ParseResultImpl((int)resultCode, data);
            Logger.LogInformation("Health Connect permission request finished; granted {Count}", granted.Count);
        }
        return await HasPermissionsAsync();
    }

    /// <summary>Total time one read may take; steps after that are skipped and the rest is returned.</summary>
    public static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(75);

    HealthReadReport? lastReport;

    public IReadOnlyList<string> LastReadSteps => lastReport?.Steps ?? [];

    public async Task<List<DailyHealthSummary>> GetDailySummariesAsync(DateOnly from, DateOnly to)
    {
        var summaries = HealthSummaryBuilder.CreateDays(from, to);

        if (GetAvailability() != HealthDataAvailability.Available)
            return summaries.Values.ToList();

        var report = new HealthReadReport(ReadBudget);
        lastReport = report;
        report.Note($"read {from:yyyy-MM-dd} to {to:yyyy-MM-dd}");
        var granted = new HashSet<string>();
        await report.RunAsync("permissions", async () => granted = await GetGrantedPermissionsAsync());
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        // Quick reads first. Daily heart rate takes most of the budget (55 s for 90 days on a
        // real phone), so it goes last; before, it left no time for exercise, HRV and weight.
        if (granted.Contains(ReadSleep))
            await report.RunAsync("sleep", () => ReadSleepAsync(start, end, summaries, report));
        if (granted.Contains(ReadExercise))
            await report.RunAsync("exercise", () => ReadExerciseAsync(granted, start, end, summaries, report));
        if (granted.Contains(ReadHrv))
            await report.RunAsync("hrv", () => ReadHrvAsync(start, end, summaries));
        if (granted.Contains(ReadWeight))
            await report.RunAsync("weight", () => ReadWeightAsync(start, end, summaries));
        await report.RunAsync("steps and heart rate", () => ReadDailyAggregatesAsync(granted, start, end, summaries, report));

        foreach (var step in report.Steps)
            Logger.LogInformation("Health Connect {Step}", step);
        return summaries.Values.OrderBy(s => s.Date).ToList();
    }

    /// <summary>Time for Sync watch's exercise-only read, including per-session heart rate.</summary>
    public static readonly TimeSpan ExerciseReadBudget = TimeSpan.FromSeconds(40);

    public async Task<ExerciseRead> GetExerciseSessionsAsync(DateOnly from, DateOnly to)
    {
        if (GetAvailability() != HealthDataAvailability.Available) return new ExerciseRead(false, []);
        var granted = await GetGrantedPermissionsAsync();
        if (!granted.Contains(ReadExercise))
        {
            Logger.LogInformation("Sync watch: exercise permission not granted");
            return new ExerciseRead(false, []);
        }

        var summaries = HealthSummaryBuilder.CreateDays(from, to);
        var report = new HealthReadReport(ExerciseReadBudget);
        report.Note($"sync watch {from:yyyy-MM-dd} to {to:yyyy-MM-dd}");
        // Not wrapped in report.RunAsync: a failed read must reach the Fitness tab as an error.
        await ReadExerciseAsync(granted, from.ToDateTime(TimeOnly.MinValue), to.AddDays(1).ToDateTime(TimeOnly.MinValue),
            summaries, report);
        foreach (var step in report.Steps)
            Logger.LogInformation("Health Connect {Step}", step);
        return new ExerciseRead(true, summaries.Values.SelectMany(d => d.Exercise).ToList());
    }

    // ---------------------------------------------------------------------
    // Steps and heart rate: aggregated by Health Connect per local day, which
    // de-duplicates overlapping phone and watch data.
    // ---------------------------------------------------------------------
    /// <summary>Days per aggregate call. 90 days of watch heart rate in one call took over 25 s.</summary>
    const int AggregateChunkDays = 15;

    async Task ReadDailyAggregatesAsync(
        HashSet<string> granted,
        DateTime start,
        DateTime end,
        Dictionary<DateOnly, DailyHealthSummary> summaries,
        HealthReadReport report)
    {
        var groups = new List<(string Name, MetricTarget[] Metrics)>();
        if (granted.Contains(ReadSteps))
            groups.Add(("steps", [new(StepsRecord.CountTotal, (d, v) => d.Steps = v)]));
        if (granted.Contains(ReadRestingHeartRate))
            groups.Add(("resting heart rate", [new(RestingHeartRateRecord.BpmAvg, (d, v) => d.RestingHeartRate = v)]));
        if (granted.Contains(ReadHeartRate))
            groups.Add(("heart rate",
            [
                new(HeartRateRecord.BpmAvg, (d, v) => d.AvgHeartRate = v),
                new(HeartRateRecord.BpmMin, (d, v) => d.MinHeartRate = v),
                new(HeartRateRecord.BpmMax, (d, v) => d.MaxHeartRate = v),
            ]));

        // Each metric group in small windows, newest first, so a slow window only loses those days.
        foreach (var (name, metrics) in groups)
        {
            int ok = 0, failed = 0, skipped = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var (chunkStart, chunkEnd) in HealthReadPlan.Chunks(start, end, AggregateChunkDays))
            {
                if (report.OutOfTime || failed >= 2) { skipped++; continue; }
                try
                {
                    await ReadAggregateChunkAsync(metrics, chunkStart, chunkEnd, summaries);
                    ok++;
                }
                catch (Exception exception)
                {
                    failed++;
                    report.Note($"{name} {chunkStart:MM-dd}..{chunkEnd.AddDays(-1):MM-dd} failed ({exception.GetType().Name}: {exception.Message})");
                }
            }
            report.Note($"{name}: {ok} ok, {failed} failed, {skipped} skipped windows in {watch.ElapsedMilliseconds} ms");
        }
    }

    sealed record MetricTarget(AggregateMetric Metric, Action<DailyHealthSummary, long?> Set);

    async Task ReadAggregateChunkAsync(MetricTarget[] metrics, DateTime start, DateTime end,
        Dictionary<DateOnly, DailyHealthSummary> summaries)
    {
        var request = new AggregateGroupByPeriodRequest(
            new JavaSet<AggregateMetric>(metrics.Select(m => m.Metric).ToList()),
            TimeRangeFilter.Between(ToLocalDateTime(start), ToLocalDateTime(end)),
            Java.Time.Period.OfDays(1)!,
            new JavaSet<DataOrigin>());
        var result = await KotlinTask.AwaitAsync(c => Client.AggregateGroupByPeriod(request, c));
        if (result == null) return;

        var buckets = result.JavaCast<Java.Util.IList>();
        for (var i = 0; i < buckets.Size(); i++)
        {
            var bucket = buckets.Get(i)!.JavaCast<AggregationResultGroupedByPeriod>();
            var bucketStart = bucket.StartTime;
            var date = new DateOnly(bucketStart.Year, bucketStart.MonthValue, bucketStart.DayOfMonth);
            if (!summaries.TryGetValue(date, out var summary)) continue;
            var values = bucket.Result;

            foreach (var target in metrics)
                target.Set(summary, GetLong(values, target.Metric));
        }
    }

    static long? GetLong(AggregationResult values, AggregateMetric? metric)
    {
        if (metric == null || !values.Contains(metric)) return null;
        var value = values.Get(metric);
        return value == null ? null : value.JavaCast<Java.Lang.Number>().LongValue();
    }

    async Task ReadSleepAsync(DateTime start, DateTime end, Dictionary<DateOnly, DailyHealthSummary> summaries,
        HealthReadReport report)
    {
        // Look back far enough to catch a night that started the evening before the range.
        var records = await ReadAllAsync<SleepSessionRecord>(start.AddHours(-18), end);
        foreach (var record in records)
            HealthSummaryBuilder.AddSleepSession(summaries, ToLocal(record.StartTime), ToLocal(record.EndTime));
        // Which apps wrote sleep, and the last nights seen, to tell a missing night from a read gap.
        var sources = records.GroupBy(r => r.Metadata.DataOrigin.PackageName)
            .Select(g => $"{g.Key} x{g.Count()}");
        var recent = records.Select(r => ToLocal(r.EndTime)).OrderByDescending(t => t).Take(7)
            .Select(t => t.ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        report.Note($"sleep: {records.Count} sessions from {string.Join(", ", sources)}; latest wake times {string.Join(", ", recent)}");
    }

    async Task ReadHrvAsync(DateTime start, DateTime end, Dictionary<DateOnly, DailyHealthSummary> summaries)
    {
        var records = await ReadAllAsync<HeartRateVariabilityRmssdRecord>(start, end);
        HealthSummaryBuilder.SetHrv(summaries, records.Select(r => (ToLocal(r.Time), r.HeartRateVariabilityMillis)));
    }

    /// <summary>Per-session heart rate and distance are only looked up for recent sessions, to keep reads fast.</summary>
    static readonly TimeSpan SessionDetailWindow = TimeSpan.FromDays(14);
    const int MaxSessionDetails = 12;

    async Task ReadExerciseAsync(HashSet<string> granted, DateTime start, DateTime end,
        Dictionary<DateOnly, DailyHealthSummary> summaries, HealthReadReport report)
    {
        var records = await ReadAllAsync<ExerciseSessionRecord>(start, end);
        var detailSince = DateTime.Now - SessionDetailWindow;
        var recent = new List<(ExerciseSessionRecord Record, ExerciseSummary Summary)>();
        foreach (var record in records)
        {
            var sessionStart = ToLocal(record.StartTime);
            var sessionEnd = ToLocal(record.EndTime);
            var summary = HealthSummaryBuilder.AddExercise(summaries, ExerciseName(record.ExerciseType), record.Title,
                sessionStart, sessionEnd, record.Metadata.Id);
            if (summary != null && sessionStart >= detailSince && summary.Minutes >= 5)
                recent.Add((record, summary));
        }
        report.Note($"exercise: {records.Count} sessions");

        // Newest first; stop when the read is out of time or a lookup hangs.
        int done = 0, failed = 0;
        foreach (var (record, summary) in recent.OrderByDescending(r => r.Summary.Start).Take(MaxSessionDetails))
        {
            if (report.OutOfTime || failed >= 2) break;
            try
            {
                await ReadSessionDetailsAsync(granted, record, summary);
                done++;
            }
            catch (Exception exception)
            {
                failed++;
                report.Note($"workout heart rate/distance failed ({exception.GetType().Name}: {exception.Message})");
            }
        }
        report.Note($"workout heart rate/distance: {done} of {recent.Count} recent sessions");
    }

    async Task ReadSessionDetailsAsync(HashSet<string> granted, ExerciseSessionRecord record, ExerciseSummary summary)
    {
        var metrics = new List<AggregateMetric>();
        if (granted.Contains(ReadHeartRate))
        {
            metrics.Add(HeartRateRecord.BpmAvg);
            metrics.Add(HeartRateRecord.BpmMax);
        }
        if (granted.Contains(ReadDistance)) metrics.Add(DistanceRecord.DistanceTotal);
        if (metrics.Count == 0) return;

        var request = new AggregateRequest(
            new JavaSet<AggregateMetric>(metrics),
            TimeRangeFilter.Between(record.StartTime, record.EndTime),
            new JavaSet<DataOrigin>());
        var result = await KotlinTask.AwaitAsync(c => Client.Aggregate(request, c));
        if (result == null) return;
        var values = result.JavaCast<AggregationResult>();

        if (granted.Contains(ReadHeartRate))
        {
            summary.AvgHeartRate = GetLong(values, HeartRateRecord.BpmAvg);
            summary.MaxHeartRate = GetLong(values, HeartRateRecord.BpmMax);
        }
        if (granted.Contains(ReadDistance) && values.Contains(DistanceRecord.DistanceTotal)
            && values.Get(DistanceRecord.DistanceTotal) is Java.Lang.Object distance)
        {
            summary.DistanceMiles = distance.JavaCast<AndroidX.Health.Connect.Client.Units.Length>().Miles;
        }
    }

    /// <summary>Names for exercise types, from the library's own constants.</summary>
    static string ExerciseName(int type)
    {
        if (type == ExerciseSessionRecord.ExerciseTypeRunning) return "running";
        if (type == ExerciseSessionRecord.ExerciseTypeRunningTreadmill) return "running_treadmill";
        if (type == ExerciseSessionRecord.ExerciseTypeWalking) return "walking";
        if (type == ExerciseSessionRecord.ExerciseTypeHiking) return "hiking";
        if (type == ExerciseSessionRecord.ExerciseTypeStrengthTraining) return "strength_training";
        if (type == ExerciseSessionRecord.ExerciseTypeWeightlifting) return "weightlifting";
        if (type == ExerciseSessionRecord.ExerciseTypeCalisthenics) return "calisthenics";
        if (type == ExerciseSessionRecord.ExerciseTypeStretching) return "stretching";
        if (type == ExerciseSessionRecord.ExerciseTypeYoga) return "yoga";
        if (type == ExerciseSessionRecord.ExerciseTypePilates) return "pilates";
        if (type == ExerciseSessionRecord.ExerciseTypeHighIntensityIntervalTraining) return "hiit";
        if (type == ExerciseSessionRecord.ExerciseTypeBiking) return "biking";
        if (type == ExerciseSessionRecord.ExerciseTypeBikingStationary) return "stationary_biking";
        if (type == ExerciseSessionRecord.ExerciseTypeElliptical) return "elliptical";
        if (type == ExerciseSessionRecord.ExerciseTypeRowingMachine) return "rowing_machine";
        if (type == ExerciseSessionRecord.ExerciseTypeGuidedBreathing) return "guided_breathing";
        if (type == ExerciseSessionRecord.ExerciseTypeWheelchair) return "wheelchair";
        if (type == ExerciseSessionRecord.ExerciseTypeOtherWorkout) return "other";
        return HealthSummaryBuilder.ExerciseTypeName(type);
    }

    async Task ReadWeightAsync(DateTime start, DateTime end, Dictionary<DateOnly, DailyHealthSummary> summaries)
    {
        var records = await ReadAllAsync<WeightRecord>(start, end);
        HealthSummaryBuilder.SetWeights(summaries, records.Select(r => (ToLocal(r.Time), r.Weight.Pounds)));
    }

    async Task<List<T>> ReadAllAsync<T>(DateTime start, DateTime end)
        where T : Java.Lang.Object
    {
        var recordType = Kotlin.Jvm.JvmClassMappingKt.GetKotlinClass(Java.Lang.Class.FromType(typeof(T)));
        var filter = TimeRangeFilter.Between(ToInstant(start), ToInstant(end));
        var records = new List<T>();
        string? pageToken = null;
        do
        {
            var request = new ReadRecordsRequest(recordType, filter, new JavaSet<DataOrigin>(), true, 1000, pageToken);
            var result = await KotlinTask.AwaitAsync(c => Client.ReadRecords(request, c));
            if (result == null) break;
            var response = result.JavaCast<ReadRecordsResponse>();
            foreach (var item in response.Records)
            {
                if (item is Java.Lang.Object javaObject)
                    records.Add(javaObject.JavaCast<T>());
            }
            pageToken = response.PageToken;
        } while (!string.IsNullOrEmpty(pageToken));
        return records;
    }

    static Java.Time.Instant ToInstant(DateTime local) =>
        Java.Time.Instant.OfEpochMilli(new DateTimeOffset(local).ToUnixTimeMilliseconds())!;

    static Java.Time.LocalDateTime ToLocalDateTime(DateTime local) =>
        Java.Time.LocalDateTime.Of(local.Year, local.Month, local.Day, local.Hour, local.Minute)!;

    static DateTime ToLocal(Java.Time.Instant instant) =>
        DateTimeOffset.FromUnixTimeMilliseconds(instant.ToEpochMilli()).LocalDateTime;
}
