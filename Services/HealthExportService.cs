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

    /// <summary>Recent app errors to include in the export, so problems are visible remotely.</summary>
    public Func<IReadOnlyList<string>>? RecentErrors { get; set; }

    public Action<Exception>? ExportFailed { get; set; }

    /// <summary>Check-ins, workouts, measurements, tests and weather to include.</summary>
    public ITrackerStore? Trackers { get; set; }

    /// <summary>Refreshes stored weather before export: (today, days) → error message or null.</summary>
    public Func<DateOnly, int, Task<string?>>? RefreshWeather { get; set; }

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

        string? weatherError = null;
        if (RefreshWeather != null)
        {
            try { weatherError = await RefreshWeather(today, days); }
            catch (Exception exception) { weatherError = exception.Message; }
        }

        TrackerExport? trackers = null;
        string? trackerError = null;
        if (Trackers != null)
        {
            try
            {
                // Workouts recorded on the watch count toward the plan without manual entry.
                await WorkoutImporter.ImportAsync(Trackers, health.SelectMany(h => h.Exercise));

                trackers = new TrackerExport(
                    await Trackers.GetCheckInsAsync(from, today),
                    await Trackers.GetWorkoutsAsync(from, today),
                    await Trackers.GetMeasurementsAsync(from, today),
                    await Trackers.GetFitnessTestsAsync(from, today),
                    await Trackers.GetWeatherAsync(from, today),
                    await Trackers.GetFoodAsync(from, today));
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "Reading trackers failed during export");
                trackerError = $"Trackers: {exception.Message}";
            }
        }

        var errors = RecentErrors?.Invoke()?.ToList() ?? [];
        if (trackerError != null) errors.Insert(0, trackerError);
        return BuildJson(DateTime.UtcNow, TimeZoneInfo.Local.Id, from, today, health, migraines, healthError,
            errors, trackers, weatherError);
    }


    public static string BuildJson(
        DateTime exportedAtUtc,
        string timeZone,
        DateOnly from,
        DateOnly to,
        IEnumerable<DailyHealthSummary> health,
        IEnumerable<MigraineEntry> migraines,
        string? healthError = null,
        IReadOnlyList<string>? appErrors = null,
        TrackerExport? trackers = null,
        string? weatherError = null)
    {
        var healthByDate = health.ToDictionary(h => h.Date);
        var migraineList = migraines.OrderBy(m => m.StartTime).ToList();
        trackers ??= TrackerExport.Empty;
        var checkIns = trackers.CheckIns.GroupBy(c => c.Date).ToDictionary(g => g.Key, g => g.Last());
        var weather = trackers.Weather.GroupBy(w => w.Date).ToDictionary(g => g.Key, g => g.Last());
        var workouts = trackers.Workouts.OrderBy(w => w.Date).ToList();
        var measurements = trackers.Measurements.OrderBy(m => m.Date).ToList();
        var tests = trackers.FitnessTests.OrderBy(t => t.Date).ToList();
        var food = (trackers.Food ?? []).OrderBy(f => f.Time).ToList();

        var days = new List<object>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            healthByDate.TryGetValue(date, out var h);
            var dayStart = date.ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);
            var dayMigraines = migraineList
                .Where(m => m.StartTime < dayEnd && (m.EndTime ?? DateTime.MaxValue) >= dayStart)
                .ToList();

            var key = DailyCheckIn.Key(date);
            checkIns.TryGetValue(key, out var c);
            weather.TryGetValue(key, out var w);
            var dayWorkouts = workouts.Where(x => DateOnly.FromDateTime(x.Date) == date).ToList();
            var dayMeasurement = measurements.LastOrDefault(m => DateOnly.FromDateTime(m.Date) == date);
            var dayFood = food.Where(f => DateOnly.FromDateTime(f.Time) == date).ToList();

            days.Add(new
            {
                date = key,
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
                plannedSession = WorkoutPlan.PlannedFor(date.DayOfWeek) ?? "Off",
                workouts = dayWorkouts.Count > 0 ? dayWorkouts.Select(x => x.SessionType) : null,
                waistInches = Round(dayMeasurement?.WaistInches),
                loggedWeightLbs = Round(dayMeasurement?.WeightLbs),
                checkIn = c == null ? null : new
                {
                    caffeineCups = c.CaffeineCups,
                    lastCaffeine = Clock(c.LastCaffeineTime),
                    waterGlasses = c.WaterGlasses,
                    firstMeal = Clock(c.FirstMealTime),
                    alcoholDrinks = c.AlcoholDrinks,
                    stress = c.Stress,
                    supplements = new { riboflavin = c.Riboflavin, bComplex = c.BComplex, coQ10 = c.CoQ10, magnesium = c.Magnesium },
                    allSupplements = c.AllSupplementsTaken,
                    proteinGrams = c.ProteinGrams,
                    proteinStatus = c.ProteinGrams.HasValue ? TrackerRules.Protein(c.ProteinGrams).ToString() : null,
                    mobilityDone = c.MobilityDone,
                    notes = c.Notes,
                },
                meals = dayFood.Count > 0
                    ? dayFood.Select(f => new
                    {
                        time = f.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
                        text = f.Text,
                        proteinGrams = f.ProteinGrams,
                    })
                    : null,
                mealProteinGrams = FoodLog.ProteinTotal(dayFood),
                weather = w == null ? null : new
                {
                    pressureMeanHpa = w.PressureMeanHpa,
                    pressureMinHpa = w.PressureMinHpa,
                    pressureMaxHpa = w.PressureMaxHpa,
                    pressureChangeHpa = w.PressureChangeHpa,
                    pressureMaxDrop6hHpa = w.PressureMaxDrop6hHpa,
                    tempMaxF = w.TempMaxF,
                    tempMinF = w.TempMinF,
                    humidityPct = w.HumidityMeanPct,
                    hours = w.Hours,
                },
            });
        }

        var payload = new
        {
            exportedAt = exportedAtUtc,
            timeZone,
            from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            healthConnectError = healthError,
            weatherError,
            appErrors = appErrors is { Count: > 0 } ? appErrors : null,
            notes = "Times are local. sleepHours sums sleep sessions that ended on that date (the night before it). Heart rate values are beats per minute. Pressure is sea-level hPa from Open-Meteo for the phone's coarse location; pressureMaxDrop6hHpa is the biggest 6-hour fall ending that day. Progress photos stay on the phone and are only counted here. meals are free-text food entries with rough portions; proteinGrams on a meal is the user's own estimate.",
            plan = new
            {
                start = WorkoutPlan.PlanStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                weekly = "Mon Strength A, Tue Run, Wed Strength B, Thu Run, Fri Strength C, Sat off, Sun Mobility",
                proteinTargetGrams = new[] { TrackerRules.ProteinMinGrams, TrackerRules.ProteinMaxGrams },
            },
            weeks = Weeks(from, to, workouts, trackers.CheckIns),
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
            }),
            workouts = workouts.Select(x => new
            {
                id = x.Id,
                start = x.Date,
                type = x.SessionType,
                source = x.IsDetected ? "watch" : "manual",
                watchExerciseType = x.ExerciseType,
                avgHeartRate = x.AvgHeartRate,
                maxHeartRate = x.MaxHeartRate,
                weekInBlock = WorkoutPlan.WeekInBlock(DateOnly.FromDateTime(x.Date)),
                durationMinutes = x.DurationMinutes,
                distanceMiles = x.DistanceMiles,
                notes = x.Notes,
                sets = x.Sets.Count > 0
                    ? x.Sets.Select(set => new { exercise = set.Exercise, reps = set.Reps, weightLbs = set.WeightLbs, seconds = set.Seconds })
                    : null,
            }),
            measurements = measurements.Select(m => new
            {
                date = m.Date,
                waistInches = m.WaistInches,
                weightLbs = m.WeightLbs,
                photoCount = m.PhotoList.Count,
                notes = m.Notes,
            }),
            fitnessTests = tests.Select(t => new
            {
                date = t.Date,
                toeTouchInches = t.ToeTouchInches,
                pushUps = t.PushUps,
                plankSeconds = t.PlankSeconds,
                notes = t.Notes,
            }),
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    static string? Clock(TimeSpan? time) =>
        time.HasValue ? time.Value.ToString(@"hh\:mm", CultureInfo.InvariantCulture) : null;

    static List<object> Weeks(DateOnly from, DateOnly to, List<WorkoutSession> workouts, List<DailyCheckIn> checkIns)
    {
        var weeks = new List<object>();
        for (var start = WorkoutPlan.WeekStart(from); start <= to; start = start.AddDays(7))
        {
            var progress = WorkoutPlan.Progress(start, workouts, checkIns);
            weeks.Add(new
            {
                weekStart = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                block = WorkoutPlan.Block(start),
                weekInBlock = WorkoutPlan.WeekInBlock(start),
                sessionsDone = progress.Done,
                sessionsPlanned = WeekProgress.Planned,
                strength = progress.StrengthDone,
                runs = progress.RunsDone,
                mobility = progress.MobilityDone,
            });
        }
        return weeks;
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
            ExportFailed?.Invoke(exception);
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

/// <summary>Tracker data for one export window.</summary>
public record TrackerExport(
    List<DailyCheckIn> CheckIns,
    List<WorkoutSession> Workouts,
    List<BodyMeasurement> Measurements,
    List<FitnessTest> FitnessTests,
    List<WeatherDay> Weather,
    List<FoodEntry>? Food = null)
{
    public static TrackerExport Empty => new([], [], [], [], []);
}
