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
    public static string StrengthSessionFor(DateTime start, IEnumerable<WorkoutSession> existing)
    {
        var planned = WorkoutPlan.PlannedFor(start.DayOfWeek);
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
    public static async Task<int> ImportAsync(ITrackerStore store, IEnumerable<ExerciseSummary> sessions)
    {
        var candidates = sessions
            .Where(s => !string.IsNullOrEmpty(s.Id))
            .Select(s => (Session: s, Category: CategoryFor(s.Type)))
            .Where(x => x.Category != null && IsLongEnough(x.Category, x.Session.Minutes))
            .OrderBy(x => x.Session.Start)
            .ToList();
        if (candidates.Count == 0) return 0;

        var from = WorkoutPlan.WeekStart(DateOnly.FromDateTime(candidates[0].Session.Start));
        var to = DateOnly.FromDateTime(candidates[^1].Session.Start);
        var existing = await store.GetAllWorkoutsAsync(from, to);
        var created = 0;

        foreach (var (session, category) in candidates)
        {
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
                    linked = true;
                }
            }

            if (match != null)
            {
                if (match.Hidden) continue;
                if (!Refresh(match, session) && !linked) continue;
                await store.SaveWorkoutAsync(match);
                continue;
            }

            var workout = new WorkoutSession
            {
                Date = session.Start,
                SessionType = category == StrengthCategory ? StrengthSessionFor(session.Start, existing) : category!,
                Source = WorkoutSession.HealthConnectSource,
                ExternalId = session.Id,
                ExerciseType = session.Type,
                Notes = session.Title,
            };
            Refresh(workout, session);
            await store.SaveWorkoutAsync(workout);
            existing.Add(workout);
            created++;
        }
        return created;
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
