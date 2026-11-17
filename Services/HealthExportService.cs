using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HealthPulse.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthPulse.Services;

/// <summary>
/// Builds the health + migraine export and writes it to the chosen
/// destination (a Google Drive file on Android).
/// </summary>
public class HealthExportService
{
    public const string FileName = "healthpulse-export.json";
    public const int DefaultDays = 90;
    public static readonly TimeSpan AutoExportInterval = TimeSpan.FromHours(3);
    public const string LastExportKey = "export_last_utc";
    public const string LastErrorKey = "export_last_error";

    public IHealthDataService HealthData;
    public IMigraineService Migraines;
    public IExportDestination Destination;
    public ISettingsStore Settings;
    public ILogger<HealthExportService> Logger;
    public SemaphoreSlim ExportLock = new(1, 1);

    public HealthExportService(
        IHealthDataService healthData,
        IMigraineService migraines,
        IExportDestination destination,
        ISettingsStore settings,
        ILogger<HealthExportService>? logger = null)
    {
        HealthData = healthData;
        Migraines = migraines;
        Destination = destination;
        Settings = settings;
        Logger = logger ?? NullLogger<HealthExportService>.Instance;
    }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DateTime? LastExportUtc
    {
        get
        {
            var text = Settings.GetString(LastExportKey);
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value)
                ? value
                : null;
        }
    }

    public string? LastError => Settings.GetString(LastErrorKey);

    public async Task<string> BuildJsonAsync(DateOnly today, int days = DefaultDays)
    {
        var from = today.AddDays(-(days - 1));

        List<DailyHealthSummary> health = [];
        string? healthError = null;
        try
        {
            if (HealthData.GetAvailability() != HealthDataAvailability.Available)
                healthError = "Health Connect is not available on this device";
            else if (!await HealthData.HasPermissionsAsync())
                healthError = "Health Connect permissions not granted";
            else
                health = await HealthData.GetDailySummariesAsync(from, today);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Health Connect read failed during export");
            healthError = exception.Message;
        }

        var migraines = await Migraines.GetEntriesForRangeAsync(from, today);
        return BuildJson(DateTime.UtcNow, TimeZoneInfo.Local.Id, from, today, health, migraines, healthError);
    }

    public static string BuildJson(
        DateTime exportedAtUtc,
        string timeZone,
        DateOnly from,
        DateOnly to,
        IEnumerable<DailyHealthSummary> health,
        IEnumerable<MigraineEntry> migraines,
        string? healthError = null)
    {
        var healthByDate = health.ToDictionary(h => h.Date);
        var migraineList = migraines.OrderBy(m => m.StartTime).ToList();

        var days = new List<object>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            healthByDate.TryGetValue(date, out var h);
            var dayStart = date.ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);
            var dayMigraines = migraineList
                .Where(m => m.StartTime < dayEnd && (m.EndTime ?? DateTime.MaxValue) >= dayStart)
                .ToList();

            days.Add(new
            {
                date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                weekday = date.DayOfWeek.ToString(),
                sleepHours = Round(h?.SleepHours, 2),
                sleepStart = h?.SleepStart,
                sleepEnd = h?.SleepEnd,
                steps = h?.Steps,
                restingHeartRate = Round(h?.RestingHeartRate),
                avgHeartRate = Round(h?.AvgHeartRate),
                minHeartRate = h?.MinHeartRate,
                maxHeartRate = h?.MaxHeartRate,
                hrvRmssdMs = Round(h?.HrvRmssdMs),
                weightLbs = Round(h?.WeightLbs),
                exerciseMinutes = h != null && h.Exercise.Count > 0 ? Math.Round(h.Exercise.Sum(e => e.Minutes)) : (double?)null,
                exercise = h != null && h.Exercise.Count > 0
                    ? h.Exercise.Select(e => new { type = e.Type, title = e.Title, start = e.Start, minutes = Math.Round(e.Minutes) })
                    : null,
                migraine = dayMigraines.Count > 0,
                migraineMaxPain = dayMigraines.Count > 0 ? dayMigraines.Max(m => m.PeakSeverity) : (int?)null,
                triptanTaken = dayMigraines.Any(m => m.TriptanTaken && m.TriptanTime.HasValue
                    && m.TriptanTime.Value >= dayStart && m.TriptanTime.Value < dayEnd) ? true : (bool?)null,
            });
        }

        var payload = new
        {
            exportedAt = exportedAtUtc,
            timeZone,
            from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            healthConnectError = healthError,
            notes = "Times are local. sleepHours sums sleep sessions that ended on that date (the night before it). Heart rate values are beats per minute.",
            days,
            migraines = migraineList.Select(m => new
            {
                id = m.Id,
                start = m.StartTime,
                end = m.EndTime,
                ongoing = m.IsOngoing,
                durationHours = m.DurationHours,
                peakPain = m.PeakSeverity,
                aura = m.Aura,
                nausea = m.Nausea,
                lightSensitivity = m.LightSensitivity,
                soundSensitivity = m.SoundSensitivity,
                triptanTaken = m.TriptanTaken,
                triptanTime = m.TriptanTime,
                triptanRelief2h = m.TriptanRelief,
                otherMeds = m.OtherMeds,
                triggers = m.TriggerList,
                notes = m.Notes
            })
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    static double? Round(double? value, int digits = 1) =>
        value.HasValue ? Math.Round(value.Value, digits) : null;

    /// <summary>Exports now. Returns null on success or an error message.</summary>
    public async Task<string?> ExportNowAsync()
    {
        if (!Destination.HasDestination)
            return "Choose an export file first.";

        await ExportLock.WaitAsync();
        try
        {
            var json = await BuildJsonAsync(DateOnly.FromDateTime(DateTime.Today));
            await Destination.WriteAsync(json);
            Settings.SetString(LastExportKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            Settings.SetString(LastErrorKey, null);
            Logger.LogInformation("Export written");
            return null;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Export failed");
            Settings.SetString(LastErrorKey, exception.Message);
            return exception.Message;
        }
        finally
        {
            ExportLock.Release();
        }
    }

    /// <summary>Called when the app opens or resumes, and after saving a migraine.</summary>
    public async Task ExportIfDueAsync(bool force = false)
    {
        if (!Destination.HasDestination) return;
        var last = LastExportUtc;
        if (!force && last.HasValue && DateTime.UtcNow - last.Value < AutoExportInterval) return;
        await ExportNowAsync();
    }
}
