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
    public const string LastWarningKey = "export_last_warning";

    public IHealthDataService HealthData;
    public IMigraineService Migraines;
    public IExportDestination Destination;
    public ISettingsStore Settings;
    public ILogger<HealthExportService> Logger;
    public SemaphoreSlim ExportLock = new(1, 1);

    /// <summary>Recent app errors to include in the export, so problems are visible remotely.</summary>
    public Func<IReadOnlyList<string>>? RecentErrors { get; set; }

    public Action<Exception>? ExportFailed { get; set; }

    /// <summary>Time limits for each step, so one stuck call can't hang the export.</summary>
    public TimeSpan HealthTimeout { get; set; } = TimeSpan.FromSeconds(150);
    public TimeSpan WeatherTimeout { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan TrackerTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan UploadTimeout { get; set; } = TimeSpan.FromSeconds(90);
    public TimeSpan PhotoTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Reads a local meal photo for upload (null when the file is gone). Photos aren't uploaded without it.</summary>
    public Func<string, Task<byte[]?>>? ReadPhoto { get; set; }
    /// <summary>How long Export now waits for an export that's already running.</summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Reports which step the export is on, for the Health tab.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Problems in one step that didn't stop the export (health or weather timed out, say).</summary>
    public List<string> LastWarnings { get; } = [];

    void Status(string text) => StatusChanged?.Invoke(text);

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

    /// <summary>Set when the last export was written but part of it was missing.</summary>
    public string? LastWarning => Settings.GetString(LastWarningKey);

    public async Task<string> BuildJsonAsync(DateOnly today, int days = DefaultDays)
    {
        var from = today.AddDays(-(days - 1));

        LastWarnings.Clear();
        List<DailyHealthSummary> health = [];
        string? healthError = null;
        try
        {
            Status("Reading Health Connect…");
            if (HealthData.GetAvailability() != HealthDataAvailability.Available)
                healthError = "Health Connect is not available on this device";
            else if (!await Timeouts.Limit(HealthData.HasPermissionsAsync(), HealthTimeout, "Checking Health Connect permissions"))
                healthError = "Health Connect permissions not granted";
            else
                health = await Timeouts.Limit(HealthData.GetDailySummariesAsync(from, today), HealthTimeout, "Reading Health Connect");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Health Connect read failed during export");
            healthError = exception.Message;
            if (exception is TimeoutException) LastWarnings.Add(exception.Message);
        }

        Status("Reading migraines…");
        var migraines = await Timeouts.Limit(Migraines.GetEntriesForRangeAsync(from, today), TrackerTimeout, "Reading migraines");

        string? weatherError = null;
        if (RefreshWeather != null)
        {
            Status("Updating weather…");
            try { weatherError = await Timeouts.Limit(RefreshWeather(today, days), WeatherTimeout, "Updating weather"); }
            catch (Exception exception)
            {
                weatherError = exception.Message;
                if (exception is TimeoutException) LastWarnings.Add(exception.Message);
            }
        }

        TrackerExport? trackers = null;
        string? trackerError = null;
        if (Trackers != null)
        {
            Status("Reading trackers…");
            try
            {
                trackers = await Timeouts.Limit(ReadTrackersAsync(Trackers, health, from, today), TrackerTimeout, "Reading trackers");
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "Reading trackers failed during export");
                trackerError = $"Trackers: {exception.Message}";
                LastWarnings.Add(trackerError);
            }
        }

        var errors = RecentErrors?.Invoke()?.ToList() ?? [];
        if (trackerError != null) errors.Insert(0, trackerError);
        IReadOnlyList<string>? healthSteps = null;
        try { healthSteps = HealthData.LastReadSteps; } catch { /* diagnostics only */ }
        return BuildJson(DateTime.UtcNow, TimeZoneInfo.Local.Id, from, today, health, migraines, healthError,
            errors, trackers, weatherError, healthSteps);
    }

    static async Task<TrackerExport> ReadTrackersAsync(ITrackerStore store, List<DailyHealthSummary> health,
        DateOnly from, DateOnly today)
    {
        // Workouts recorded on the watch count toward the plan without manual entry.
        await WorkoutImporter.ImportAsync(store, health.SelectMany(h => h.Exercise));
        return new TrackerExport(
            await store.GetCheckInsAsync(from, today),
            await store.GetWorkoutsAsync(from, today),
            await store.GetMeasurementsAsync(from, today),
            await store.GetFitnessTestsAsync(from, today),
            await store.GetWeatherAsync(from, today),
            await store.GetFoodAsync(from, today),
            await store.GetPlanSwapsAsync(from, today));
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
        string? weatherError = null,
        IReadOnlyList<string>? healthSteps = null)
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
        var swaps = trackers.Swaps ?? [];

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
                plannedSession = WorkoutPlan.PlannedFor(date, swaps) ?? "Off",
                planSwap = WorkoutPlan.IsSwapped(date, swaps) ? WorkoutPlan.SwapNote(date, swaps) : null,
                workouts = dayWorkouts.Count > 0 ? dayWorkouts.Select(x => x.SessionType) : null,
                waistInches = Round(dayMeasurement?.WaistInches),
                loggedWeightLbs = Round(dayMeasurement?.WeightLbs),
                checkIn = c == null ? null : new
                {
                    caffeineCups = c.CaffeineCups,
                    lastCaffeine = Clock(c.LastCaffeineTime),
                    waterOunces = WaterLog.Ounces(c),
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
                        portion = FoodLog.FindPortion(f.Portion)?.Name,
                        portionSize = FoodLog.FindPortion(f.Portion)?.Size,
                        ingredients = f.IngredientList.Count > 0 ? f.IngredientList : null,
                        proteinGrams = f.ProteinGrams,
                        photo = f.HasPhoto && !string.IsNullOrEmpty(f.PhotoDriveId) ? MealPhotos.ExportPath(f) : null,
                        photoDriveFileId = f.HasPhoto ? f.PhotoDriveId : null,
                        photoPendingUpload = f.HasPhoto && string.IsNullOrEmpty(f.PhotoDriveId) ? true : (bool?)null,
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
            healthConnectSteps = healthSteps is { Count: > 0 } ? healthSteps : null,
            weatherError,
            appErrors = appErrors is { Count: > 0 } ? appErrors : null,
            notes = "Times are local. sleepHours sums sleep sessions that ended on that date (the night before it). Heart rate values are beats per minute. Pressure is sea-level hPa from Open-Meteo for the phone's coarse location; pressureMaxDrop6hHpa is the biggest 6-hour fall ending that day. Progress photos stay on the phone and are only counted here. meals are free-text food entries with rough portions; proteinGrams on a meal is the user's own estimate. A meal can also have a portion picked from a fixed list (portionSize says what it means, e.g. Medium bowl is about 1.5 to 2 cups), ingredients ticked from chips (main ingredients, not a complete list), and a photo: photo is the path relative to this file's folder (My Drive/HealthPulse/meal-photos/...), and photoDriveFileId is its Google Drive file id. photoPendingUpload means a photo was taken but goes up with the next export. Use the photo, portion and ingredients together with the text to estimate nutrients. Workout sets have rir (reps in reserve, 0 to 3 where 3 means 3 or more) when rated; weights are per dumbbell. strength summarizes each exercise's progression (weight, estimated one-rep max, volume) and the app's next recommendation; strength.rule explains the rule.",
            plan = new
            {
                start = WorkoutPlan.PlanStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                weekly = "Mon Strength A, Tue Run, Wed Strength B, Thu Run, Fri Strength C, Sat off, Sun Mobility",
                alternatives = "A workout's alternative (e.g. Treadmill, Stationary bike, Body-weight version) means it was done indoors or as a lighter version and still counts as its session. planSwap marks a day whose plan was swapped with another day of the same week; plannedSession is the plan after the swap.",
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
                alternative = x.Alternative,
                source = x.IsDetected ? "watch" : "manual",
                watchExerciseType = x.ExerciseType,
                avgHeartRate = x.AvgHeartRate,
                maxHeartRate = x.MaxHeartRate,
                weekInBlock = WorkoutPlan.WeekInBlock(DateOnly.FromDateTime(x.Date)),
                durationMinutes = x.DurationMinutes,
                distanceMiles = x.DistanceMiles,
                notes = x.Notes,
                sets = x.Sets.Count > 0
                    ? x.Sets.Select(set => new
                    {
                        exercise = set.Exercise,
                        reps = set.Reps,
                        weightLbs = set.WeightLbs,
                        seconds = set.Seconds,
                        rir = set.RepsInReserve,
                    })
                    : null,
                volumeLbs = x.Sets.Any(set => set.WeightLbs.HasValue && set.Reps.HasValue)
                    ? x.Sets.Sum(set => (set.WeightLbs ?? 0) * (set.Reps ?? 0))
                    : (double?)null,
            }),
            strength = Strength(workouts, to),
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

    /// <summary>Per-exercise progression and the next recommendation, for agents reading the export.</summary>
    static object? Strength(List<WorkoutSession> workouts, DateOnly to)
    {
        var progress = StrengthProgress.Summaries(workouts, to);
        if (progress.Count == 0) return null;
        static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new
        {
            rule = StrengthProgress.RuleText,
            exercises = progress.Select(p => new
            {
                exercise = p.Exercise.Name,
                weighted = p.Exercise.Weighted,
                timed = p.Exercise.Timed,
                perSide = p.Exercise.PerSide,
                lowerBody = StrengthProgress.IsLowerBody(p.Exercise.Name),
                sessions = p.Sessions.Count,
                firstDate = Day(p.First.Date),
                lastDate = Day(p.Last.Date),
                firstWeightLbs = p.First.WeightLbs,
                currentWeightLbs = p.Last.WeightLbs,
                weightChangeLbs = p.First.WeightLbs.HasValue && p.Last.WeightLbs.HasValue ? p.Last.WeightLbs - p.First.WeightLbs : null,
                firstE1rmLbs = p.First.E1rmLbs,
                lastE1rmLbs = p.Last.E1rmLbs,
                bestE1rmLbs = p.Sessions.Max(s => s.E1rmLbs),
                sessionsAtCurrentWeight = p.Last.WeightLbs.HasValue ? p.StalledSessions : (int?)null,
                next = new
                {
                    date = Day(p.NextDate),
                    action = p.Next.Action,
                    weightLbs = p.Next.WeightLbs,
                    targetMin = p.Next.Target.Min,
                    targetMax = p.Next.Target.Max,
                    targetUnit = p.Next.Target.Seconds ? "seconds" : "reps",
                    reason = p.Next.Reason,
                },
                history = p.Sessions.Select(s => new
                {
                    date = Day(s.Date),
                    weightLbs = s.WeightLbs,
                    reps = s.Sets.Any(x => x.Reps.HasValue) ? s.Sets.Select(x => x.Reps) : null,
                    seconds = s.Sets.Any(x => x.Seconds.HasValue) ? s.Sets.Select(x => x.Seconds) : null,
                    rir = s.Sets.Any(x => x.RepsInReserve.HasValue) ? s.Sets.Select(x => x.RepsInReserve) : null,
                    e1rmLbs = s.E1rmLbs,
                    volumeLbs = s.VolumeLbs,
                }),
            }),
        };
    }

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

        if (!await ExportLock.WaitAsync(TimeSpan.Zero))
        {
            Status("Waiting for the export that's already running…");
            if (!await ExportLock.WaitAsync(LockTimeout))
            {
                var busy = $"Another export has been running for over {Timeouts.Describe(LockTimeout)}. Close and reopen the app, then try again.";
                Settings.SetString(LastErrorKey, busy);
                ExportFailed?.Invoke(new TimeoutException(busy));
                return busy;
            }
        }
        try
        {
            var photoWarning = await UploadMealPhotosAsync(DateOnly.FromDateTime(DateTime.Today));
            var json = await BuildJsonAsync(DateOnly.FromDateTime(DateTime.Today));
            if (photoWarning != null) LastWarnings.Add(photoWarning);
            Status("Uploading to Google Drive…");
            await Timeouts.Limit(Destination.WriteAsync(json), UploadTimeout, "Uploading to Google Drive");
            Settings.SetString(LastExportKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            Settings.SetString(LastErrorKey, null);
            Settings.SetString(LastWarningKey, LastWarnings.Count > 0 ? string.Join(" ", LastWarnings) : null);
            foreach (var warning in LastWarnings)
                ExportFailed?.Invoke(new TimeoutException($"Export written without some data: {warning}"));
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
            Status("");
            ExportLock.Release();
        }
    }

    /// <summary>
    /// Copies new meal photos to Drive before the export is built, so the export can point to them.
    /// A failure only delays the photos to the next export. Returns a warning or null.
    /// </summary>
    async Task<string?> UploadMealPhotosAsync(DateOnly today)
    {
        if (Trackers == null || ReadPhoto == null || !Destination.CanUploadPhotos) return null;
        try
        {
            Status("Uploading meal photos…");
            var from = today.AddDays(-(DefaultDays - 1));
            var warning = await Timeouts.Limit(
                MealPhotos.UploadPendingAsync(Trackers, Destination, ReadPhoto, from, today),
                PhotoTimeout, "Uploading meal photos");
            if (warning != null) Logger.LogWarning("Meal photo upload: {Warning}", warning);
            return warning;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Meal photo upload failed");
            return $"Meal photos: {exception.Message}";
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
    List<FoodEntry>? Food = null,
    List<PlanSwap>? Swaps = null)
{
    public static TrackerExport Empty => new([], [], [], [], []);
}
