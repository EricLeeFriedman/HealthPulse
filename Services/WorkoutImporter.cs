using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>
/// Turns exercise sessions from the watch (via Health Connect) into workouts
/// matched to the plan, so they count without manual entry.
/// </summary>
public static class WorkoutImporter
{
    /// <summary>Shorter sessions are usually accidental or warm-ups.</summary>
    public const double MinRunOrStrengthMinutes = 10;
    public const double MinMobilityMinutes = 5;
    public const double MinOtherMinutes = 15;

    const string StrengthCategory = "strength";

    /// <summary>
    /// The plan category for a Health Connect exercise type: Run, "strength",
    /// Mobility, Other, or null for types that aren't imported (walks, hikes).
    /// </summary>
    public static string? CategoryFor(string exerciseType) => exerciseType switch
    {
        "running" or "running_treadmill" => WorkoutPlan.Run,
        "strength_training" or "weightlifting" or "calisthenics" => StrengthCategory,
        "stretching" or "yoga" or "pilates" => WorkoutPlan.Mobility,
        // Daily dog walks are tracked as steps, not as plan sessions.
        "walking" or "hiking" or "wheelchair" or "guided_breathing" => null,
        _ => WorkoutPlan.Other
    };

    /// <summary>
    /// Indoor cardio from the watch that stands in for a run on a run day (rain, cold),
    /// and the alternative name it is saved with.
    /// </summary>
    public static string? IndoorRunAlternative(string exerciseType) => exerciseType switch
    {
        "running_treadmill" => WorkoutAlternatives.Treadmill,
        "stationary_biking" => WorkoutAlternatives.Bike,
        "biking" => "Bike ride",
        "stair_climbing" or "stair_climbing_machine" => WorkoutAlternatives.Stairs,
        "elliptical" => "Elliptical",
        "rowing_machine" => "Rowing machine",
        "hiit" => "Indoor cardio",
        _ => null
    };

    public static bool IsLongEnough(string category, double minutes) => category switch
    {
        WorkoutPlan.Mobility => minutes >= MinMobilityMinutes,
        WorkoutPlan.Other => minutes >= MinOtherMinutes,
        _ => minutes >= MinRunOrStrengthMinutes
    };

    /// <summary>
    /// Which strength session a detected strength workout was: the day's planned one
    /// if it's a strength day, otherwise the first of A, B, C not yet done that week.
    /// </summary>
    public static string StrengthSessionFor(DateTime start, IEnumerable<WorkoutSession> existing,
        IEnumerable<PlanSwap>? swaps = null)
    {
        var planned = WorkoutPlan.PlannedFor(DateOnly.FromDateTime(start), swaps);
        if (planned != null && WorkoutPlan.IsStrength(planned)) return planned;

        var weekStart = WorkoutPlan.WeekStart(DateOnly.FromDateTime(start));
        var done = existing
            .Where(w => !w.Hidden && WorkoutPlan.IsStrength(w.SessionType)
                        && WorkoutPlan.WeekStart(DateOnly.FromDateTime(w.Date)) == weekStart)
            .Select(w => w.SessionType)
            .ToHashSet();
        return new[] { WorkoutPlan.StrengthA, WorkoutPlan.StrengthB, WorkoutPlan.StrengthC }
            .FirstOrDefault(s => !done.Contains(s)) ?? WorkoutPlan.StrengthC;
    }

    static bool SameCategory(string sessionType, string category) =>
        category == StrengthCategory ? WorkoutPlan.IsStrength(sessionType) : sessionType == category;

    /// <summary>
    /// Creates or updates workouts for detected sessions. A session already imported
    /// (same Health Connect id) only gets its measurements refreshed; a manual workout of
    /// the same kind on the same day is linked instead of duplicated. Returns the number
    /// of new workouts.
    /// </summary>
    public static async Task<int> ImportAsync(ITrackerStore store, IEnumerable<ExerciseSummary> sessions) =>
        (await ImportDetailedAsync(store, sessions)).Created.Count;

    /// <summary>
    /// Like <see cref="ImportAsync"/>, but says what happened to each session: added as a new
    /// workout, linked to one logged by hand, or skipped (too short, or deleted earlier).
    /// </summary>
    public static async Task<WorkoutImportResult> ImportDetailedAsync(ITrackerStore store, IEnumerable<ExerciseSummary> sessions)
    {
        var result = new WorkoutImportResult();
        var all = sessions.Where(s => !string.IsNullOrEmpty(s.Id)).ToList();
        foreach (var session in all)
        {
            if (CategoryFor(session.Type) is { } c && !IsLongEnough(c, session.Minutes))
                result.TooShort.Add(session);
        }
        var candidates = all
            .Select(s => (Session: s, Category: CategoryFor(s.Type)))
            .Where(x => x.Category != null && IsLongEnough(x.Category, x.Session.Minutes))
            .OrderBy(x => x.Session.Start)
            .ToList();
        if (candidates.Count == 0) return result;

        var from = WorkoutPlan.WeekStart(DateOnly.FromDateTime(candidates[0].Session.Start));
        var to = DateOnly.FromDateTime(candidates[^1].Session.Start);
        var existing = await store.GetAllWorkoutsAsync(from, to);
        var swaps = await store.GetPlanSwapsAsync(from, to.AddDays(6));

        foreach (var (session, watchCategory) in candidates)
        {
            var category = watchCategory;
            string? alternative = null;
            if (category == WorkoutPlan.Other && RunsIndoors(session, existing, swaps) is { } indoor)
            {
                category = WorkoutPlan.Run;
                alternative = indoor;
            }
            else if (session.Type == "running_treadmill")
            {
                alternative = WorkoutAlternatives.Treadmill;
            }

            var match = existing.FirstOrDefault(w => w.ExternalId == session.Id);
            var linked = false;
            if (match == null)
            {
                var day = DateOnly.FromDateTime(session.Start);
                match = existing.FirstOrDefault(w => w.ExternalId == null && !w.Hidden
                    && DateOnly.FromDateTime(w.Date) == day && SameCategory(w.SessionType, category!));
                if (match != null)
                {
                    match.ExternalId = session.Id;
                    match.Alternative ??= alternative;
                    linked = true;
                }
            }

            if (match != null)
            {
                if (match.Hidden)
                {
                    result.Deleted.Add(session);
                    continue;
                }
                if (linked) result.Linked.Add((session, match));
                else result.AlreadyLogged.Add((session, match));
                if (!Refresh(match, session) && !linked) continue;
                await store.SaveWorkoutAsync(match);
                continue;
            }

            var workout = new WorkoutSession
            {
                Date = session.Start,
                SessionType = category == StrengthCategory ? StrengthSessionFor(session.Start, existing, swaps) : category!,
                Alternative = alternative,
                Source = WorkoutSession.HealthConnectSource,
                ExternalId = session.Id,
                ExerciseType = session.Type,
                Notes = session.Title,
            };
            Refresh(workout, session);
            await store.SaveWorkoutAsync(workout);
            existing.Add(workout);
            result.Created.Add((session, workout));
        }
        return result;
    }

    /// <summary>
    /// What to show after tapping Sync watch: what was added or linked, and when nothing
    /// was, what Health Connect has for today, so a workout that hasn't reached Health
    /// Connect yet can be told apart from one the app skipped.
    /// </summary>
    public static string SyncSummary(WorkoutImportResult result, IReadOnlyCollection<ExerciseSummary> sessions, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        bool IsToday(ExerciseSummary s) => DateOnly.FromDateTime(s.Start) == today;
        static string Describe(ExerciseSummary s) => $"{s.Title ?? s.Type.Replace('_', ' ')} at {s.Start:h:mm tt}";
        var parts = new List<string>();

        foreach (var (session, workout) in result.Created)
            parts.Add($"Added {workout.SessionType} from your watch ({Describe(session)}, {Math.Round(session.Minutes)} min).");
        foreach (var (session, workout) in result.Linked)
            parts.Add($"Linked your watch's {Describe(session)} to the {workout.SessionType} you logged.");
        if (result.Created.Count + result.Linked.Count > 0)
        {
            if (result.Created.Concat(result.Linked).Any(x => WorkoutPlan.IsStrength(x.Workout.SessionType)))
                parts.Add("Tap it to add sets and weights (optional).");
            return string.Join(" ", parts);
        }

        foreach (var session in result.TooShort.Where(IsToday))
            parts.Add($"Today's {Describe(session)} was only {Math.Round(session.Minutes)} min, under the minimum, so it wasn't added. Log it by hand if it counts.");
        foreach (var session in result.Deleted.Where(IsToday))
            parts.Add($"Today's {Describe(session)} was deleted here earlier, so it isn't added again.");
        foreach (var (session, workout) in result.AlreadyLogged.Where(x => IsToday(x.Session)))
            parts.Add($"Today's {Describe(session)} is already logged as {workout.SessionType}.");
        if (parts.Count > 0) return string.Join(" ", parts);

        var workoutsToday = sessions.Where(s => IsToday(s) && CategoryFor(s.Type) != null).ToList();
        if (workoutsToday.Count > 0) return $"Up to date. Last checked {now:h:mm tt}.";

        var latest = sessions.Where(s => CategoryFor(s.Type) != null).OrderByDescending(s => s.Start).FirstOrDefault();
        var latestText = latest == null ? "" : $" The latest is {latest.Title ?? latest.Type.Replace('_', ' ')} on {latest.Start:ddd MMM d}.";
        return $"Health Connect has no workout from today yet.{latestText} If your watch app shows one, it hasn't shared it with Health Connect yet; open that app to let it sync, then tap Sync watch again. Checked {now:h:mm tt}.";
    }

    /// <summary>
    /// The alternative name when an indoor cardio session falls on a run day that has no
    /// run from the watch yet, so a rainy-day bike ride counts as the run. Otherwise null.
    /// </summary>
    static string? RunsIndoors(ExerciseSummary session, List<WorkoutSession> existing, List<PlanSwap> swaps)
    {
        if (IndoorRunAlternative(session.Type) is not { } alternative) return null;
        var day = DateOnly.FromDateTime(session.Start);
        if (WorkoutPlan.PlannedFor(day, swaps) != WorkoutPlan.Run) return null;
        var watchRunToday = existing.Any(w => w.ExternalId != null && w.ExternalId != session.Id
            && DateOnly.FromDateTime(w.Date) == day && w.SessionType == WorkoutPlan.Run);
        return watchRunToday ? null : alternative;
    }

    /// <summary>Copies the watch's measurements onto the workout. Returns true when anything changed.</summary>
    static bool Refresh(WorkoutSession workout, ExerciseSummary session)
    {
        var changed = false;
        var minutes = (int)Math.Round(session.Minutes);
        if (workout.DurationMinutes == null || (workout.IsDetected && workout.DurationMinutes != minutes))
        {
            workout.DurationMinutes = minutes;
            changed = true;
        }
        if (session.DistanceMiles is { } miles && miles > 0.05)
        {
            var rounded = Math.Round(miles, 2);
            if (workout.DistanceMiles == null || (workout.IsDetected && workout.DistanceMiles != rounded))
            {
                workout.DistanceMiles = rounded;
                changed = true;
            }
        }
        if (session.AvgHeartRate is { } avg && workout.AvgHeartRate != Math.Round(avg))
        {
            workout.AvgHeartRate = Math.Round(avg);
            changed = true;
        }
        if (session.MaxHeartRate is { } max && workout.MaxHeartRate != Math.Round(max))
        {
            workout.MaxHeartRate = Math.Round(max);
            changed = true;
        }
        if (workout.ExerciseType != session.Type)
        {
            workout.ExerciseType = session.Type;
            changed = true;
        }
        return changed;
    }
}

/// <summary>What one import did with the watch's sessions.</summary>
public class WorkoutImportResult
{
    public List<(ExerciseSummary Session, WorkoutSession Workout)> Created { get; } = [];
    public List<(ExerciseSummary Session, WorkoutSession Workout)> Linked { get; } = [];
    public List<(ExerciseSummary Session, WorkoutSession Workout)> AlreadyLogged { get; } = [];
    public List<ExerciseSummary> TooShort { get; } = [];
    public List<ExerciseSummary> Deleted { get; } = [];
}
