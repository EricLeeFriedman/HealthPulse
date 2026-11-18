using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>An exercise in a planned session.</summary>
/// <param name="Weighted">Logs a dumbbell weight.</param>
/// <param name="Timed">Logs seconds instead of reps.</param>
/// <param name="PerSide">Reps or seconds are per side, arm or leg.</param>
public record ExerciseTemplate(string Name, bool Weighted, bool Timed = false, bool PerSide = false)
{
    public string Unit => (Timed ? "seconds" : "reps") + (PerSide ? " each side" : "");
}

/// <summary>This week's progress against the plan's six sessions.</summary>
public record WeekProgress(DateOnly WeekStart, int StrengthDone, int RunsDone, bool MobilityDone)
{
    public const int StrengthPlanned = 3;
    public const int RunsPlanned = 2;
    public const int Planned = 6;

    public int Done => Math.Min(StrengthDone, StrengthPlanned) + Math.Min(RunsDone, RunsPlanned) + (MobilityDone ? 1 : 0);

    public string Text =>
        $"{Done} of {Planned} sessions this week: strength {StrengthDone}/{StrengthPlanned}, runs {RunsDone}/{RunsPlanned}, mobility {(MobilityDone ? "done" : "not yet")}";
}

/// <summary>
/// Eric's 12-week plan: Mon Strength A, Tue Run, Wed Strength B, Thu Run,
/// Fri Strength C, Sat off, Sun mobility. Four-week blocks starting Nov 23, 2026.
/// </summary>
public static class WorkoutPlan
{
    public static readonly DateOnly PlanStart = new(2026, 11, 23);

    public const string StrengthA = "Strength A";
    public const string StrengthB = "Strength B";
    public const string StrengthC = "Strength C";
    public const string Run = "Run";
    public const string Mobility = "Mobility";
    public const string Other = "Other";

    public static readonly string[] SessionTypes = [StrengthA, StrengthB, StrengthC, Run, Mobility, Other];

    public static readonly string[] MobilityRoutine =
    [
        "Child's pose, 1 min",
        "Half-kneeling hip flexor stretch, 45 s per side",
        "Lying hamstring stretch with a towel, 45 s per side",
        "90/90 hip switches, 8 per side",
        "Open-book thoracic rotation, 8 per side",
        "Deep squat hold, 3 x 20 s",
        "Figure-4 glute stretch, 45 s per side"
    ];

    /// <summary>The routine as numbered steps, for the Today tab.</summary>
    public static string MobilityRoutineText =>
        "About 15 minutes, same routine every time. Move slowly and stay below pain.\n"
        + string.Join("\n", MobilityRoutine.Select((step, i) => $"{i + 1}. {step}"));

    public static bool IsStrength(string sessionType) =>
        sessionType is StrengthA or StrengthB or StrengthC;

    /// <summary>The planned session for a weekday, or null for Saturday (off).</summary>
    public static string? PlannedFor(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => StrengthA,
        DayOfWeek.Tuesday => Run,
        DayOfWeek.Wednesday => StrengthB,
        DayOfWeek.Thursday => Run,
        DayOfWeek.Friday => StrengthC,
        DayOfWeek.Sunday => Mobility,
        _ => null
    };

    /// <summary>Monday of the week containing the date.</summary>
    public static DateOnly WeekStart(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Week 1 to 4 within the current block (week 1 before the plan starts).</summary>
    public static int WeekInBlock(DateOnly date)
    {
        var weeks = WeeksSinceStart(date);
        return weeks < 0 ? 1 : weeks % 4 + 1;
    }

    /// <summary>Block 1, 2, 3 (each 4 weeks).</summary>
    public static int Block(DateOnly date)
    {
        var weeks = WeeksSinceStart(date);
        return weeks < 0 ? 1 : weeks / 4 + 1;
    }

    static int WeeksSinceStart(DateOnly date)
    {
        var days = WeekStart(date).DayNumber - PlanStart.DayNumber;
        return days < 0 ? -1 : days / 7;
    }

    public static int TargetSets(DateOnly date) => WeekInBlock(date) == 1 ? 2 : 3;

    public static string StrengthTarget(DateOnly date) => WeekInBlock(date) switch
    {
        1 => "2 x 10 with weights that feel easy. Core moves: 2 x 30 s or 2 x 8. Rest 60 s.",
        2 => "3 x 10 with the same weights. Rest 60 s.",
        3 => "3 x 10 to 12. Hit 12 on every set? Go up one dumbbell size next time. Rest 60 to 90 s.",
        _ => "3 x 8 to 10 with heavier weights. Rest 90 s. Friday: push-up and plank test."
    };

    public static string RunTarget(DateOnly date)
    {
        if (Block(date) >= 2)
            return "Build from 20 to 30 minutes nonstop at an easy pace, toward 2 miles.";
        return WeekInBlock(date) switch
        {
            1 => "Run 3 min, walk 1 min, 5 rounds (20 min).",
            2 => "Run 5 min, walk 1 min, 4 rounds (24 min).",
            3 => "Run 8 min, walk 1 min, 3 rounds (27 min).",
            _ => "Tue: 20 min nonstop. Thu: easy warm-up, then a timed mile."
        };
    }

    public static string TargetFor(string sessionType, DateOnly date) => sessionType switch
    {
        StrengthA or StrengthB or StrengthC => StrengthTarget(date),
        Run => RunTarget(date),
        Mobility => "15 minutes: " + string.Join("; ", MobilityRoutine) + ".",
        _ => ""
    };

    public static IReadOnlyList<ExerciseTemplate> Exercises(string sessionType, DateOnly date) => sessionType switch
    {
        StrengthA =>
        [
            new("Goblet squat", Weighted: true),
            new("Push-ups", Weighted: false),
            new("Reverse lunge / split squat", Weighted: true, PerSide: true),
            new("Half-kneeling shoulder press", Weighted: true, PerSide: true),
            new("Dead bug", Weighted: false, PerSide: true),
        ],
        StrengthB =>
        [
            new("One-arm dumbbell row", Weighted: true, PerSide: true),
            WeekInBlock(date) <= 2
                ? new ExerciseTemplate("Glute bridge", Weighted: false)
                : new ExerciseTemplate("Dumbbell Romanian deadlift", Weighted: true),
            new("Chest-supported reverse fly", Weighted: true),
            new("Bird dog", Weighted: false, PerSide: true),
            new("Side plank", Weighted: false, Timed: true, PerSide: true),
        ],
        StrengthC =>
        [
            new("Step-ups", Weighted: true, PerSide: true),
            new("Dumbbell floor press", Weighted: true),
            new("One-arm dumbbell row", Weighted: true, PerSide: true),
            new("Biceps curl", Weighted: true),
            new("Triceps extension", Weighted: true),
            new("Farmer carry", Weighted: true, Timed: true),
            new("Front plank", Weighted: false, Timed: true),
        ],
        _ => []
    };

    /// <summary>Counts sessions in the week starting <paramref name="weekStart"/> (a Monday).</summary>
    public static WeekProgress Progress(DateOnly weekStart, IEnumerable<WorkoutSession> sessions, IEnumerable<DailyCheckIn> checkIns)
    {
        var end = weekStart.AddDays(7);
        bool InWeek(DateOnly d) => d >= weekStart && d < end;
        var week = sessions.Where(s => InWeek(DateOnly.FromDateTime(s.Date))).ToList();
        var mobility = week.Any(s => s.SessionType == Mobility)
            || checkIns.Any(c => c.MobilityDone && InWeek(c.DateValue));
        return new WeekProgress(weekStart,
            week.Count(s => IsStrength(s.SessionType)),
            week.Count(s => s.SessionType == Run),
            mobility);
    }

    /// <summary>The most recent sets for an exercise, newest session first.</summary>
    public static List<WorkoutSet> LastSets(string exercise, IEnumerable<WorkoutSession> history) =>
        history.OrderByDescending(s => s.Date)
            .Select(s => s.Sets.Where(x => x.Exercise == exercise).OrderBy(x => x.Order).ToList())
            .FirstOrDefault(sets => sets.Count > 0) ?? [];

    /// <summary>"Last: 3 x 10 @ 25 lb" style hint for an exercise.</summary>
    public static string LastTimeText(IReadOnlyList<WorkoutSet> sets)
    {
        if (sets.Count == 0) return "";
        var first = sets[0];
        var amount = first.Seconds.HasValue ? $"{first.Seconds} s" : $"{first.Reps}";
        var weight = first.WeightLbs.HasValue ? $" @ {first.WeightLbs:0.#} lb" : "";
        return $"Last time: {sets.Count} x {amount}{weight}";
    }

    /// <summary>
    /// The plan's progression rule: in week 3 or later, when every set reached 12 reps,
    /// go up one dumbbell size next time.
    /// </summary>
    public static bool ShouldGoUp(IReadOnlyList<WorkoutSet> lastSets, DateOnly date) =>
        WeekInBlock(date) >= 3
        && lastSets.Count > 0
        && lastSets.All(s => s.WeightLbs.HasValue && s.Reps is >= 12);
}
