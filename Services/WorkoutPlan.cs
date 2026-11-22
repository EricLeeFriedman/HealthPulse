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
/// <summary>One move of the mobility routine, with how to do it and where to see it.</summary>
public record MobilityMove(string Name, string Dose, string How, string DemoUrl, string DemoSource)
{
    public string Step => $"{Name}, {Dose}";
}

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

    /// <summary>
    /// The mobility routine, each move with a plain-language how-to and a link to a
    /// reputable page with photos of the move. Links checked Nov 2026.
    /// </summary>
    public static readonly MobilityMove[] MobilityMoves =
    [
        new("Child's pose", "1 min",
            "Kneel on the floor with your big toes touching and your knees about hip-width apart or wider. Sit your hips back toward your heels and walk your hands forward until your forehead rests on the floor or a pillow. Let your chest sink and breathe slowly.",
            "https://www.acefitness.org/resources/everyone/exercise-library/227/childs-pose/", "ACE"),
        new("Half-kneeling hip flexor stretch", "45 s per side",
            "Kneel on your right knee (put a pillow under it) with your left foot flat in front, both knees bent about 90 degrees. Squeeze your right glute, tuck your tailbone a little, then shift your hips forward until you feel a stretch in the front of your right hip. Keep your chest tall and don't arch your lower back. Switch sides.",
            "https://www.acefitness.org/resources/everyone/exercise-library/142/kneeling-hip-flexor-stretch/", "ACE"),
        new("Lying hamstring stretch with a towel", "45 s per side",
            "Lie on your back with your legs straight. Loop a towel around the ball of one foot and hold both ends. Keeping that knee almost straight, pull the leg up until you feel a stretch behind your thigh, not behind your knee. Keep the other leg flat on the floor. No towel? Rest your heel up a door frame, as in the photo.",
            "https://www.acefitness.org/resources/everyone/exercise-library/235/supine-hamstrings-stretch/", "ACE"),
        new("90/90 hip switches", "8 per side",
            "Sit on the floor with your knees bent, feet flat and wider than your hips, hands on the floor behind you. Let both knees drop to the right so your right shin lies in front of you and your left shin out to the side, both knees bent about 90 degrees. Sit tall for a breath, then lift your knees and let them drop to the left. Move slowly and lean on your hands as much as you need.",
            "https://health.clevelandclinic.org/90-90-stretch/", "Cleveland Clinic"),
        new("Open-book thoracic rotation", "8 per side",
            "Lie on your left side with your knees bent up to hip height and stacked, arms straight out in front, palms together. Keep your knees together and sweep your top arm up and over to the other side like opening a book, turning your chest and head to follow it as far as is comfortable. Breathe out, then bring it back. Do all 8, then switch sides.",
            "https://us.humankinetics.com/blogs/excerpt/try-this-exercise-opening-a-book", "Human Kinetics"),
        new("Deep squat hold", "3 x 20 s",
            "Stand with your feet a little wider than your shoulders, toes turned out. Sit your hips straight down between your heels as low as is comfortable, chest up, elbows pressing gently on your inner knees. Hold 20 seconds, stand up, and repeat 3 times. Tipping backward? Hold a door frame or counter, or put a folded towel under your heels.",
            "https://www.hingehealth.com/resources/articles/benefits-of-deep-squat/", "Hinge Health"),
        new("Figure-4 glute stretch", "45 s per side",
            "Lie on your back with your knees bent and feet flat. Cross your right ankle over your left knee. Reach through and hold the back of your left thigh, then pull it toward your chest until you feel a stretch in your right buttock. Keep your head and shoulders relaxed on the floor. Switch sides.",
            "https://www.hingehealth.com/resources/articles/figure-four/", "Hinge Health"),
    ];

    /// <summary>Each move with its dose, such as "Child's pose, 1 min".</summary>
    public static readonly string[] MobilityRoutine = MobilityMoves.Select(m => m.Step).ToArray();

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

    /// <summary>
    /// The planned session for a date after any day swaps, or null for a rest day.
    /// </summary>
    public static string? PlannedFor(DateOnly date, IEnumerable<PlanSwap>? swaps)
    {
        var key = DailyCheckIn.Key(date);
        var swap = swaps?.LastOrDefault(s => s.Date == key);
        if (swap == null) return PlannedFor(date.DayOfWeek);
        return string.IsNullOrEmpty(swap.SessionType) ? null : swap.SessionType;
    }

    /// <summary>"Run" or "Rest" for showing a day's plan.</summary>
    public static string SessionName(string? sessionType) => sessionType ?? "Rest";

    /// <summary>
    /// Days of the same week (Monday to Sunday) that <paramref name="date"/> can swap with:
    /// every other day whose plan is different. Days already swapped are left out, so
    /// a swap is undone before making another.
    /// </summary>
    public static List<DateOnly> SwapOptions(DateOnly date, IReadOnlyCollection<PlanSwap> swaps)
    {
        var mine = PlannedFor(date, swaps);
        var weekStart = WeekStart(date);
        var options = new List<DateOnly>();
        for (var i = 0; i < 7; i++)
        {
            var other = weekStart.AddDays(i);
            if (other == date || IsSwapped(other, swaps)) continue;
            if (PlannedFor(other, swaps) == mine) continue;
            options.Add(other);
        }
        return options;
    }

    public static bool IsSwapped(DateOnly date, IEnumerable<PlanSwap> swaps)
    {
        var key = DailyCheckIn.Key(date);
        return swaps.Any(s => s.Date == key);
    }

    /// <summary>The two rows that swap the plans of <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static List<PlanSwap> Swap(DateOnly a, DateOnly b, IEnumerable<PlanSwap> swaps)
    {
        if (a == b) throw new ArgumentException("Pick a different day to swap with.");
        if (WeekStart(a) != WeekStart(b)) throw new ArgumentException("Swaps stay within the same week.");
        var list = swaps.ToList();
        var planA = PlannedFor(a, list);
        var planB = PlannedFor(b, list);
        return
        [
            new PlanSwap { Date = DailyCheckIn.Key(a), SessionType = planB ?? "", SwappedWith = DailyCheckIn.Key(b) },
            new PlanSwap { Date = DailyCheckIn.Key(b), SessionType = planA ?? "", SwappedWith = DailyCheckIn.Key(a) },
        ];
    }

    /// <summary>The dates to clear to undo the swap that includes <paramref name="date"/>.</summary>
    public static List<string> UndoSwap(DateOnly date, IEnumerable<PlanSwap> swaps)
    {
        var key = DailyCheckIn.Key(date);
        var swap = swaps.FirstOrDefault(s => s.Date == key);
        if (swap == null) return [];
        return string.IsNullOrEmpty(swap.SwappedWith) ? [key] : [key, swap.SwappedWith];
    }

    /// <summary>"Swapped with Wed (was Run)" style note for a swapped day, or "".</summary>
    public static string SwapNote(DateOnly date, IEnumerable<PlanSwap> swaps)
    {
        var key = DailyCheckIn.Key(date);
        var swap = swaps.FirstOrDefault(s => s.Date == key);
        if (swap == null) return "";
        var was = SessionName(PlannedFor(date.DayOfWeek));
        if (!DateOnly.TryParseExact(swap.SwappedWith, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var other))
            return $"Changed from {was}.";
        var otherName = other.ToString("dddd", System.Globalization.CultureInfo.InvariantCulture);
        return $"Swapped with {otherName} (was {was}).";
    }

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
        var amounts = sets.Select(s => s.Seconds.HasValue ? $"{s.Seconds} s" : $"{s.Reps}").ToList();
        // "3 x 10" when every set matched, otherwise each set: "3 x 10/10/8".
        var amount = amounts.Distinct().Count() == 1 ? amounts[0] : string.Join("/", amounts);
        var weight = first.WeightLbs.HasValue ? $" @ {first.WeightLbs:0.#} lb" : "";
        var effort = sets.Where(s => s.RepsInReserve.HasValue).Select(s => s.RepsInReserve!.Value).ToList();
        var left = effort.Count == 0 ? "" : $" ({StrengthProgress.EffortText(effort.Min())} left on the hardest set)";
        return $"Last time: {sets.Count} x {amount}{weight}{left}";
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
