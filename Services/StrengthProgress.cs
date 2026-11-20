using System.Globalization;
using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>A rep (or seconds) range for an exercise on a given plan week.</summary>
public record RepRange(int Min, int Max, bool Seconds)
{
    public string Text => (Min == Max ? $"{Max}" : $"{Min} to {Max}") + (Seconds ? " s" : " reps");
}

/// <summary>What to do with an exercise next session, and why.</summary>
/// <param name="Action">One of the <see cref="StrengthProgress"/> action names, e.g. "go_up".</param>
/// <param name="WeightLbs">Weight to use next time (per dumbbell), or null for body-weight moves.</param>
/// <param name="Target">Reps or seconds to aim for on every set.</param>
/// <param name="Reason">One or two plain sentences for the workout screen and the export.</param>
public record Recommendation(string Action, double? WeightLbs, RepRange Target, string Reason)
{
    public bool GoUp => Action is StrengthProgress.GoUpAction or StrengthProgress.AddRepsAction or StrengthProgress.AddTimeAction;
    public bool BackOff => Action == StrengthProgress.BackOffAction;

    /// <summary>"⬆ Go up to 30 lb" style tag for the workout screen.</summary>
    public string Tag => Action switch
    {
        StrengthProgress.GoUpAction => $"⬆ Go up to {WeightLbs:0.#} lb",
        StrengthProgress.AddRepsAction => "⬆ Add reps",
        StrengthProgress.AddTimeAction => "⬆ Hold longer",
        StrengthProgress.BackOffAction => $"⬇ Drop to {WeightLbs:0.#} lb",
        StrengthProgress.RepeatAction => WeightLbs.HasValue ? $"↻ Repeat {WeightLbs:0.#} lb" : "↻ Repeat",
        StrengthProgress.StartAction => "New exercise",
        _ => WeightLbs.HasValue ? $"= Stay at {WeightLbs:0.#} lb" : "= Same again",
    };
}

/// <summary>One logged session of one exercise.</summary>
public record ExerciseSession(DateOnly Date, IReadOnlyList<WorkoutSet> Sets)
{
    /// <summary>The heaviest weight used in the session (per dumbbell).</summary>
    public double? WeightLbs => Sets.Max(s => s.WeightLbs);

    /// <summary>Sets done at the session's top weight: the ones that decide progression.</summary>
    public List<WorkoutSet> WorkSets => WeightLbs is { } top ? Sets.Where(s => s.WeightLbs == top).ToList() : [.. Sets];

    /// <summary>Best estimated one-rep max of the session, or null when no weighted rep sets.</summary>
    public double? E1rmLbs => Sets.Select(StrengthProgress.E1rm).Where(e => e.HasValue).Max();

    /// <summary>Total pounds moved: weight x reps over every set.</summary>
    public double? VolumeLbs => Sets.Any(s => s.WeightLbs.HasValue && s.Reps.HasValue)
        ? Sets.Sum(s => (s.WeightLbs ?? 0) * (s.Reps ?? 0))
        : null;
}

/// <summary>An exercise's history and what to do next, for the Fitness tab and the export.</summary>
public record ExerciseProgress(
    ExerciseTemplate Exercise,
    IReadOnlyList<ExerciseSession> Sessions,
    Recommendation Next,
    DateOnly NextDate,
    int StalledSessions)
{
    public ExerciseSession First => Sessions[0];
    public ExerciseSession Last => Sessions[^1];

    /// <summary>"Goblet squat: 25 → 35 lb since Nov 23 • ⬆ Go up to 40 lb" style line.</summary>
    public string Text
    {
        get
        {
            string change;
            if (Last.WeightLbs.HasValue)
            {
                change = First.WeightLbs.HasValue && First.WeightLbs != Last.WeightLbs
                    ? $"{First.WeightLbs:0.#} → {Last.WeightLbs:0.#} lb since {First.Date:MMM d}"
                    : $"{Last.WeightLbs:0.#} lb";
            }
            else
            {
                var unit = Exercise.Timed ? " s" : " reps";
                var best = (Sessions.Select(s => StrengthProgress.BestAmount(s.Sets)).Max()) ?? 0;
                var firstBest = StrengthProgress.BestAmount(First.Sets) ?? 0;
                change = best != firstBest ? $"{firstBest} → {best}{unit} best set" : $"{best}{unit} best set";
            }
            var stalled = StalledSessions >= 3 && Next.Action == StrengthProgress.HoldAction ? $" • same for {StalledSessions} sessions" : "";
            return $"{Exercise.Name}: {change} • {Next.Tag}{stalled}";
        }
    }
}

/// <summary>
/// Double progression with reps in reserve: stay at a weight until every set reaches the top
/// of the rep range with 2 or more reps left, then go up one dumbbell step. Follows the plan's
/// 4-week blocks (week 1 easy, week 2 same weights, week 3 builds to 12, week 4 heavier for 8 to 10).
/// </summary>
public static class StrengthProgress
{
    public const string StartAction = "start";
    public const string GoUpAction = "go_up";
    public const string HoldAction = "hold";
    public const string RepeatAction = "repeat";
    public const string BackOffAction = "back_off";
    public const string AddRepsAction = "add_reps";
    public const string AddTimeAction = "add_time";

    /// <summary>Choices for the effort picker; the index is the stored reps in reserve.</summary>
    public static readonly IReadOnlyList<string> EffortOptions = ["0 left", "1 left", "2 left", "3+ left"];

    public static string EffortText(int repsInReserve) => repsInReserve >= 3 ? "3+" : repsInReserve.ToString(CultureInfo.InvariantCulture);

    public const string RuleText =
        "Double progression with reps in reserve (RIR, how many more good reps were left at the end of a set; 3 means 3 or more). " +
        "Stay at a weight until every set reaches the top of the week's rep range with 2 or more reps left, then go up one step " +
        "(about 5 lb per dumbbell; 10 lb on the goblet squat once it is 30 lb or more). Two or more sets short of the bottom of the range: " +
        "repeat the weight; short again at the same weight: drop one step. Body-weight moves add reps, timed holds add 5 to 10 s. " +
        "Plan weeks: 1 easy (2 x 10), 2 same weights (3 x 10), 3 builds to 3 x 12, 4 heavier for 3 x 8 to 10. " +
        "Weights are per dumbbell. e1rmLbs is an estimated one-rep max: weight x (1 + (reps + RIR) / 30).";

    static readonly HashSet<string> CoreReps = new(StringComparer.OrdinalIgnoreCase) { "Dead bug", "Bird dog" };

    static readonly HashSet<string> LowerBody = new(StringComparer.OrdinalIgnoreCase)
    {
        "Goblet squat", "Reverse lunge / split squat", "Dumbbell Romanian deadlift", "Step-ups", "Glute bridge", "Farmer carry"
    };

    public static bool IsLowerBody(string exercise) => LowerBody.Contains(exercise);

    /// <summary>The rep (or seconds) range the plan asks for on a date.</summary>
    public static RepRange RangeFor(ExerciseTemplate exercise, DateOnly date)
    {
        var week = WorkoutPlan.WeekInBlock(date);
        if (exercise.Timed)
        {
            if (exercise.Name.Equals("Farmer carry", StringComparison.OrdinalIgnoreCase)) return new(30, 40, true);
            return week == 1 ? new(20, 30, true) : new(30, 30, true);
        }
        if (CoreReps.Contains(exercise.Name)) return new(8, 8, false);
        return week switch
        {
            1 or 2 => new(10, 10, false),
            3 => new(10, 12, false),
            _ => new(8, 10, false),
        };
    }

    /// <summary>One dumbbell step up or down for an exercise at a weight.</summary>
    public static double Step(string exercise, double weightLbs) =>
        exercise.Equals("Goblet squat", StringComparison.OrdinalIgnoreCase) && weightLbs >= 30 ? 10 : 5;

    /// <summary>Estimated one-rep max (Epley, counting reps left): weight x (1 + (reps + RIR) / 30).</summary>
    public static double? E1rm(WorkoutSet set) =>
        set.WeightLbs is > 0 && set.Reps is > 0
            ? Math.Round(set.WeightLbs.Value * (1 + (set.Reps.Value + (set.RepsInReserve ?? 0)) / 30.0), 1)
            : null;

    /// <summary>Most reps (or seconds) in a single set.</summary>
    public static int? BestAmount(IEnumerable<WorkoutSet> sets) => sets.Select(Amount).Max();

    static int? Amount(WorkoutSet set) => set.Seconds ?? set.Reps;

    /// <summary>Each logged session of an exercise, oldest first.</summary>
    public static List<ExerciseSession> History(string exercise, IEnumerable<WorkoutSession> workouts) =>
        workouts
            .Where(w => !w.Hidden)
            .OrderBy(w => w.Date)
            .Select(w => new ExerciseSession(DateOnly.FromDateTime(w.Date),
                w.Sets.Where(s => s.Exercise == exercise && Amount(s).HasValue).OrderBy(s => s.Order).ToList()))
            .Where(s => s.Sets.Count > 0)
            .ToList();

    /// <summary>What to do next time for an exercise, from its history (oldest first).</summary>
    public static Recommendation Recommend(ExerciseTemplate exercise, IReadOnlyList<ExerciseSession> history, DateOnly nextDate)
    {
        var target = RangeFor(exercise, nextDate);
        if (history.Count == 0)
        {
            var load = ExerciseGuide.For(exercise.Name)?.Load;
            return new(StartAction, null, target,
                $"First time: aim for {target.Text}. {load ?? "Pick a weight where the last 2 reps are hard but your form stays clean."}".Trim());
        }

        var last = history[^1];
        var lastRange = RangeFor(exercise, last.Date);
        var lastWeek = WorkoutPlan.WeekInBlock(last.Date);
        var nextWeek = WorkoutPlan.WeekInBlock(nextDate);
        var sets = last.WorkSets;
        var amounts = sets.Select(s => Amount(s) ?? 0).ToList();
        var efforts = sets.Where(s => s.RepsInReserve.HasValue).Select(s => s.RepsInReserve!.Value).ToList();
        bool? easy = efforts.Count == 0 ? null : efforts.All(e => e >= 2);
        var veryEasy = efforts.Count == sets.Count && efforts.All(e => e >= 3);
        var maxedOut = efforts.Any(e => e == 0);
        var hitTop = amounts.All(a => a >= lastRange.Max);
        var shortSets = amounts.Count(a => a < lastRange.Min);
        var unrated = easy == null ? " Pick reps left after each set so the call is more accurate." : "";

        if (!exercise.Weighted || last.WeightLbs == null)
            return BodyWeight(exercise, last, lastRange, target, hitTop, easy, shortSets, unrated);

        var weight = last.WeightLbs.Value;
        var step = Step(exercise.Name, weight);
        var missedTwice = shortSets >= Math.Min(2, sets.Count) && history.Count >= 2
            && history[^2].WeightLbs == weight
            && history[^2].WorkSets.Count(s => (Amount(s) ?? 0) < RangeFor(exercise, history[^2].Date).Min) >= Math.Min(2, history[^2].WorkSets.Count);

        // New block: week 1 is meant to feel easy after the heavy week 4.
        if (nextWeek == 1 && lastWeek == 4)
        {
            var easier = Math.Max(0, weight - step);
            return new(HoldAction, easier, target,
                $"New block, easy week: use about {easier:0.#} lb for {target.Text} and leave 3 or more reps in the tank.");
        }

        if (shortSets >= Math.Min(2, sets.Count) && easy != true)
        {
            if (missedTwice && weight - step > 0)
                return new(BackOffAction, weight - step, target,
                    $"Short of {lastRange.Min}{(lastRange.Seconds ? " s" : " reps")} at {weight:0.#} lb two sessions running. Drop to {weight - step:0.#} lb and build back up.");
            return new(RepeatAction, weight, target,
                $"Last time {Join(amounts)} at {weight:0.#} lb. Repeat {weight:0.#} lb and aim for {target.Text}.{unrated}");
        }

        // Week 4 asks for heavier weights after week 3.
        if (nextWeek == 4 && lastWeek == 3 && !exercise.Timed && amounts.All(a => a >= 10) && !maxedOut)
            return new(GoUpAction, weight + step, target,
                $"Heavy week: you did {Join(amounts)} at {weight:0.#} lb, so go up to {weight + step:0.#} lb for {target.Text}.");

        if (hitTop && easy != false)
        {
            // The plan keeps the same weights in week 2 unless they were clearly too light.
            if (nextWeek == 2 && lastWeek == 1 && !veryEasy)
                return new(HoldAction, weight, target,
                    $"Week 2 keeps the same weights: {weight:0.#} lb for {target.Text}. Go up once all sets feel easy (3+ left).");
            var because = easy == true
                ? $"You hit {Join(amounts)} with {EffortText(efforts.Min())}+ reps left"
                : $"You hit {Join(amounts)}";
            return new(GoUpAction, weight + step, target,
                $"{because} at {weight:0.#} lb. Go up to {weight + step:0.#} lb and aim for {target.Text}; fewer reps at first is fine.{unrated}");
        }

        if (hitTop)
            return new(HoldAction, weight, target,
                $"You hit the reps at {weight:0.#} lb but it was hard. Stay there until every set has 2 or more reps left.");

        var stalled = Stalled(history);
        var stallNote = stalled >= 3
            ? $" Same weight for {stalled} sessions without reaching the top: try one extra set, slower lowering, or check sleep and protein."
            : "";
        return new(HoldAction, weight, target,
            $"Stay at {weight:0.#} lb. Go up once every set reaches {lastRange.Max} with 2 or more reps left (last time {Join(amounts)}).{stallNote}{unrated}");
    }

    static Recommendation BodyWeight(ExerciseTemplate exercise, ExerciseSession last, RepRange lastRange, RepRange target,
        bool hitTop, bool? easy, int shortSets, string unrated)
    {
        var best = BestAmount(last.WorkSets) ?? 0;
        if (exercise.Timed)
        {
            if (hitTop && easy != false)
                return new(AddTimeAction, last.WeightLbs, new(best + 5, best + 10, true),
                    $"You held {best} s{(easy == true ? " with time to spare" : "")}. Add 5 to 10 s per set next time.{unrated}");
            return new(HoldAction, last.WeightLbs, target, $"Hold {target.Text} on every set (best last time {best} s).{unrated}");
        }
        if (shortSets > 0 && easy != true)
            return new(RepeatAction, null, target,
                $"Aim for {target.Text} per set{(exercise.Name == "Push-ups" ? "; use a bench or your knees if you can't reach it" : "")}.");
        if (hitTop && easy != false)
        {
            if (best >= 20)
                return new(AddRepsAction, null, new(best, best, false),
                    $"{best} reps is plenty: make it harder (slower lowering, a lower incline or a pause) instead of adding reps.");
            return new(AddRepsAction, null, new(best + 1, best + 2, false),
                $"You did {best}{(easy == true ? " with reps to spare" : "")}. Add 1 or 2 reps per set next time.{unrated}");
        }
        return new(HoldAction, null, target, $"Aim for {target.Text} per set again (best last time {best}).{unrated}");
    }

    /// <summary>How many sessions in a row (newest back) used the same weight without reaching the top of the range.</summary>
    public static int Stalled(IReadOnlyList<ExerciseSession> history)
    {
        if (history.Count == 0 || history[^1].WeightLbs is not { } weight) return 0;
        var count = 0;
        for (var i = history.Count - 1; i >= 0 && history[i].WeightLbs == weight; i--) count++;
        return count;
    }

    static string Join(List<int> amounts) =>
        amounts.Distinct().Count() == 1 ? $"{amounts.Count} x {amounts[0]}" : string.Join("/", amounts);

    /// <summary>The exercise's plan template, or one inferred from how its sets were logged.</summary>
    public static ExerciseTemplate TemplateFor(string exercise, IEnumerable<WorkoutSet> sets)
    {
        foreach (var type in new[] { WorkoutPlan.StrengthA, WorkoutPlan.StrengthB, WorkoutPlan.StrengthC })
            foreach (var date in new[] { WorkoutPlan.PlanStart, WorkoutPlan.PlanStart.AddDays(14) })
                if (WorkoutPlan.Exercises(type, date).FirstOrDefault(t => t.Name == exercise) is { } template)
                    return template;
        var list = sets.ToList();
        return new ExerciseTemplate(exercise, list.Any(s => s.WeightLbs.HasValue), list.Any(s => s.Seconds.HasValue));
    }

    /// <summary>The next date on or after <paramref name="from"/> whose planned session includes the exercise.</summary>
    public static DateOnly NextPlannedDate(string exercise, DateOnly from)
    {
        for (var i = 0; i < 7; i++)
        {
            var day = from.AddDays(i);
            if (WorkoutPlan.PlannedFor(day.DayOfWeek) is { } type && WorkoutPlan.Exercises(type, day).Any(t => t.Name == exercise))
                return day;
        }
        return from;
    }

    /// <summary>
    /// Progress and the next step for every exercise logged in <paramref name="workouts"/>,
    /// as of <paramref name="from"/> (the next session is on or after it), in plan order.
    /// </summary>
    public static List<ExerciseProgress> Summaries(IEnumerable<WorkoutSession> workouts, DateOnly from)
    {
        var list = workouts.Where(w => !w.Hidden).ToList();
        var names = list.OrderBy(w => w.Date).SelectMany(w => w.Sets.OrderBy(s => s.Order)).Select(s => s.Exercise).Distinct().ToList();
        var result = new List<ExerciseProgress>();
        foreach (var name in names)
        {
            var history = History(name, list);
            if (history.Count == 0) continue;
            var template = TemplateFor(name, history.SelectMany(h => h.Sets));
            // A session already logged today counts as done, so look from tomorrow.
            var start = history[^1].Date >= from ? history[^1].Date.AddDays(1) : from;
            var next = NextPlannedDate(name, start);
            result.Add(new ExerciseProgress(template, history, Recommend(template, history, next), next, Stalled(history)));
        }
        return result;
    }
}
