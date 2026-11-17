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

    /// <summary>Lets the 11:45 PM export read data while the app isn't open (Android 14+).</summary>
    public const string ReadInBackground = "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND";

    public static readonly string[] Permissions =
        [ReadSteps, ReadSleep, ReadHeartRate, ReadRestingHeartRate, ReadHrv, ReadExercise, ReadWeight, ReadInBackground];

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

    public async Task<List<DailyHealthSummary>> GetDailySummariesAsync(DateOnly from, DateOnly to)
    {
        var summaries = HealthSummaryBuilder.CreateDays(from, to);

        if (GetAvailability() != HealthDataAvailability.Available)
            return summaries.Values.ToList();

        var granted = await GetGrantedPermissionsAsync();
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        await Try("aggregates", () => ReadDailyAggregatesAsync(granted, start, end, summaries));
        if (granted.Contains(ReadSleep))
            await Try("sleep", () => ReadSleepAsync(start, end, summaries));
        if (granted.Contains(ReadHrv))
            await Try("hrv", () => ReadHrvAsync(start, end, summaries));
        if (granted.Contains(ReadExercise))
            await Try("exercise", () => ReadExerciseAsync(start, end, summaries));
        if (granted.Contains(ReadWeight))
            await Try("weight", () => ReadWeightAsync(start, end, summaries));

        return summaries.Values.OrderBy(s => s.Date).ToList();
    }

    async Task Try(string what, Func<Task> read)
    {
        try
        {
            await read();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Health Connect read failed: {What}", what);
        }
    }

    // ---------------------------------------------------------------------
    // Steps and heart rate: aggregated by Health Connect per local day, which
    // de-duplicates overlapping phone and watch data.
    // ---------------------------------------------------------------------
    async Task ReadDailyAggregatesAsync(
        HashSet<string> granted,
        DateTime start,
        DateTime end,
        Dictionary<DateOnly, DailyHealthSummary> summaries)
    {
        var metrics = new List<AggregateMetric>();
        if (granted.Contains(ReadSteps)) metrics.Add(StepsRecord.CountTotal);
        if (granted.Contains(ReadHeartRate))
        {
            metrics.Add(HeartRateRecord.BpmAvg);
            metrics.Add(HeartRateRecord.BpmMin);
            metrics.Add(HeartRateRecord.BpmMax);
        }
        if (granted.Contains(ReadRestingHeartRate)) metrics.Add(RestingHeartRateRecord.BpmAvg);
        if (metrics.Count == 0) return;

        var request = new AggregateGroupByPeriodRequest(
            metrics,
            TimeRangeFilter.Between(ToLocalDateTime(start), ToLocalDateTime(end)),
            Java.Time.Period.OfDays(1)!,
            new List<DataOrigin>());
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

            summary.Steps = GetLong(values, granted.Contains(ReadSteps) ? StepsRecord.CountTotal : null);
            if (granted.Contains(ReadHeartRate))
            {
                summary.AvgHeartRate = GetLong(values, HeartRateRecord.BpmAvg);
                summary.MinHeartRate = GetLong(values, HeartRateRecord.BpmMin);
                summary.MaxHeartRate = GetLong(values, HeartRateRecord.BpmMax);
            }
            if (granted.Contains(ReadRestingHeartRate))
                summary.RestingHeartRate = GetLong(values, RestingHeartRateRecord.BpmAvg);
        }
    }

    static long? GetLong(AggregationResult values, AggregateMetric? metric)
    {
        if (metric == null || !values.Contains(metric)) return null;
        var value = values.Get(metric);
        return value == null ? null : value.JavaCast<Java.Lang.Number>().LongValue();
    }

    async Task ReadSleepAsync(DateTime start, DateTime end, Dictionary<DateOnly, DailyHealthSummary> summaries)
    {
        // Look back far enough to catch a night that started the evening before the range.
        var records = await ReadAllAsync<SleepSessionRecord>(start.AddHours(-18), end);
        foreach (var record in records)
            HealthSummaryBuilder.AddSleepSession(summaries, ToLocal(record.StartTime), ToLocal(record.EndTime));
    }

    async Task ReadHrvAsync(DateTime start, DateTime end, Dictionary<DateOnly, DailyHealthSummary> summaries)
    {
        var records = await ReadAllAsync<HeartRateVariabilityRmssdRecord>(start, end);
        HealthSummaryBuilder.SetHrv(summaries, records.Select(r => (ToLocal(r.Time), r.HeartRateVariabilityMillis)));
    }

    async Task ReadExerciseAsync(DateTime start, DateTime end, Dictionary<DateOnly, DailyHealthSummary> summaries)
    {
        var records = await ReadAllAsync<ExerciseSessionRecord>(start, end);
        foreach (var record in records)
            HealthSummaryBuilder.AddExercise(summaries, record.ExerciseType, record.Title,
                ToLocal(record.StartTime), ToLocal(record.EndTime));
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
            var request = new ReadRecordsRequest(recordType, filter, new List<DataOrigin>(), true, 1000, pageToken);
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
