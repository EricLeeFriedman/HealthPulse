namespace HealthPulse.Services;

/// <summary>A backup way to do a planned session, e.g. a treadmill when it's pouring.</summary>
/// <param name="Name">Short name, saved on the workout ("Treadmill").</param>
/// <param name="How">Step-by-step instructions matched to the day's plan.</param>
public record WorkoutAlternative(string Name, string How);

/// <summary>
/// Indoor or lighter versions of each planned session. Logging one counts as doing
/// the session: the workout keeps the planned session type and records the
/// alternative's name.
/// </summary>
public static class WorkoutAlternatives
{
    public const string Treadmill = "Treadmill";
    public const string Bike = "Stationary bike";
    public const string Stairs = "Stairs";
    public const string IndoorWalk = "Brisk indoor walk";
    public const string BodyWeight = "Body-weight version";
    public const string Short = "Short version";

    /// <summary>The alternatives for a session on a date, or none (mobility is already indoors).</summary>
    public static IReadOnlyList<WorkoutAlternative> For(string? sessionType, DateOnly date)
    {
        if (sessionType == WorkoutPlan.Run) return RunAlternatives(date);
        if (sessionType != null && WorkoutPlan.IsStrength(sessionType)) return StrengthAlternatives(sessionType, date);
        return [];
    }

    /// <summary>All names that can be saved for a session type, for the workout form's picker.</summary>
    public static IReadOnlyList<string> NamesFor(string? sessionType, DateOnly date) =>
        For(sessionType, date).Select(a => a.Name).ToList();

    static bool IsMileTest(DateOnly date) =>
        WorkoutPlan.Block(date) == 1 && WorkoutPlan.WeekInBlock(date) == 4 && date.DayOfWeek == DayOfWeek.Thursday;

    static IReadOnlyList<WorkoutAlternative> RunAlternatives(DateOnly date)
    {
        var plan = WorkoutPlan.RunTarget(date);
        var mile = IsMileTest(date)
            ? "\nTimed mile day: do the mile on the treadmill at 1% incline, or keep it for your next run outside."
            : "";
        var noMile = IsMileTest(date)
            ? "\nTimed mile day: skip the mile and do 20 easy minutes instead; test the mile on your next run outside or on a treadmill."
            : "";
        return
        [
            new(Treadmill,
                $"Same plan as outside: {plan}\n"
                + "Set the incline to 1% so it feels like running outdoors. Start around 4.5 to 5 mph for the running parts and 3 to 3.5 mph for the walk breaks, then adjust so you could still hold a conversation. "
                + "Walk 5 minutes before and after." + mile),
            new(Bike,
                $"Ride for the same total time as today's run: {plan}\n"
                + "Each running part: pedal at a moderate effort, breathing harder but still able to talk in short sentences. Each walk break: easy spinning with light resistance. "
                + "Seat at hip height so your knee is almost straight at the bottom. Spin easy 5 minutes before and after." + noMile),
            new(Stairs,
                $"Use a flight of stairs at home or in a building, for the same total time: {plan}\n"
                + "Each running part: walk up the stairs at a steady pace and come down carefully, holding the rail. Each walk break: walk slowly on the flat. "
                + "Too easy? Take the stairs faster on the way up, never two at a time going down. Stop if your knees or back complain." + noMile),
            new(IndoorWalk,
                $"No equipment, for the same total time: {plan}\n"
                + "Each running part: fast laps around the house, or march in place with high knees and swinging arms, adding 20 jumping jacks or 10 step-ups on a stair every couple of minutes. "
                + "Each walk break: easy walking. You should be breathing harder but able to talk." + noMile),
        ];
    }

    /// <summary>Body-weight swaps for each dumbbell exercise.</summary>
    static readonly Dictionary<string, string> BodyWeightSwaps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Goblet squat"] = "Body-weight squat: 3 seconds down, pause, stand up. Hold a full backpack at your chest once it gets easy.",
        ["Reverse lunge / split squat"] = "Reverse lunge with no weight; slow down to 3 seconds on the way down.",
        ["Half-kneeling shoulder press"] = "Pike push-up: hands and feet on the floor, hips high, lower your head toward the floor between your hands. Or press a full backpack or water jugs overhead.",
        ["One-arm dumbbell row"] = "Backpack row: load a backpack with books, hold it by the top handle and row it like the dumbbell. Or a towel row around a sturdy door handle.",
        ["Dumbbell Romanian deadlift"] = "Single-leg Romanian deadlift with no weight, holding a wall or chair for balance.",
        ["Chest-supported reverse fly"] = "Prone Y raise: lie face down, arms overhead in a Y, thumbs up, and lift your arms off the floor for 2 seconds.",
        ["Step-ups"] = "Step-ups with no weight, or holding a loaded backpack.",
        ["Dumbbell floor press"] = "Push-ups (hands on a couch if needed).",
        ["Biceps curl"] = "Backpack curl: hold a loaded backpack by the straps and curl it.",
        ["Triceps extension"] = "Bench dip: hands on the edge of a sturdy chair, feet on the floor, lower until your elbows bend about 90 degrees.",
        ["Farmer carry"] = "Carry a loaded backpack or two full grocery bags, or two water jugs.",
    };

    static IReadOnlyList<WorkoutAlternative> StrengthAlternatives(string sessionType, DateOnly date)
    {
        var exercises = WorkoutPlan.Exercises(sessionType, date);
        var bodyWeight = new System.Text.StringBuilder();
        bodyWeight.Append("No dumbbells (travelling or away from home). Same sets and reps as the plan, swapping each weighted move:\n");
        var number = 1;
        foreach (var exercise in exercises)
        {
            var swap = exercise.Weighted && BodyWeightSwaps.TryGetValue(exercise.Name, out var text) ? text : "As planned.";
            bodyWeight.Append($"\n{number++}. {exercise.Name}: {swap}");
        }

        var first = exercises.Take(3).Select(e => e.Name).ToList();
        var shortHow = "About 20 minutes when you're short on time or energy. Warm up 3 minutes, then do 2 sets of "
            + $"{string.Join(", ", first.Take(first.Count - 1))} and {first.LastOrDefault()}, resting 45 to 60 seconds. "
            + "It still counts; the week's other sessions carry the rest.";
        return
        [
            new(BodyWeight, bodyWeight.ToString()),
            new(Short, shortHow),
        ];
    }
}
