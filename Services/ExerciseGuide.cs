using System.Text;

namespace HealthPulse.Services;

/// <summary>How to do one exercise, and a starting weight.</summary>
public record ExerciseHowTo(string How, string Load);

/// <summary>
/// Plain-language instructions for every exercise in the plan, so the app
/// explains a session without needing to look anything up.
/// </summary>
public static class ExerciseGuide
{
    static readonly Dictionary<string, ExerciseHowTo> Guides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Goblet squat"] = new(
            "Hold one dumbbell upright against your chest with both hands. Feet shoulder-width, toes slightly out. Sit your hips down between your heels, chest up, until your thighs are about level with the floor. Push through your whole foot to stand.",
            "Start around 20 to 25 lb."),
        ["Push-ups"] = new(
            "Hands under your shoulders, body in a straight line from head to heels. Lower until your chest is a fist above the floor, then press up. Too hard? Put your hands on a bench or couch, or drop to your knees.",
            "Body weight. Stop each set 2 reps before your form breaks."),
        ["Reverse lunge / split squat"] = new(
            "Hold a dumbbell in each hand at your sides. Step one foot back and lower your back knee toward the floor until both knees are bent about 90 degrees. Push through your front heel to stand. Do all the reps on one leg, then switch.",
            "Start with no weight or 10 lb in each hand."),
        ["Half-kneeling shoulder press"] = new(
            "Kneel on one knee with the other foot flat in front. Hold a dumbbell at the shoulder on the same side as the down knee and press it straight overhead. Keep your ribs down and don't lean back. Do all the reps, then switch sides.",
            "Start around 10 to 15 lb."),
        ["Dead bug"] = new(
            "Lie on your back, arms pointing at the ceiling, knees bent 90 degrees over your hips. Press your lower back into the floor. Slowly lower one arm behind you and the opposite leg toward the floor, then bring them back and switch.",
            "Body weight. Go slow; your lower back stays flat the whole time."),
        ["One-arm dumbbell row"] = new(
            "Put one hand and the same-side knee on a bench (or one hand on a chair), back flat. Let the dumbbell hang, then pull it up toward your hip, elbow close to your side, and squeeze your shoulder blade. Lower slowly. All reps, then switch.",
            "Start around 20 to 25 lb."),
        ["Glute bridge"] = new(
            "Lie on your back, knees bent, feet flat and hip-width apart. Squeeze your glutes and lift your hips until your body is straight from knees to shoulders. Pause 1 second at the top, then lower.",
            "Body weight. Rest a dumbbell on your hips once it gets easy."),
        ["Dumbbell Romanian deadlift"] = new(
            "Stand holding the dumbbells in front of your thighs, knees slightly bent. Push your hips back and slide the weights down the front of your legs to mid-shin, back flat, until you feel your hamstrings stretch. Squeeze your glutes to stand.",
            "Start around 15 to 25 lb in each hand."),
        ["Chest-supported reverse fly"] = new(
            "Lie face down on an incline bench, or sit and lean forward with your chest near your thighs. Let your arms hang with a slight bend in the elbows. Raise them out to the sides to shoulder height, squeezing your shoulder blades together. Lower slowly.",
            "Light: 5 to 10 lb in each hand."),
        ["Bird dog"] = new(
            "On hands and knees, back flat. Reach one arm forward and the opposite leg back until both are level with your body. Hold 2 seconds without twisting your hips, return, and switch sides.",
            "Body weight."),
        ["Side plank"] = new(
            "Lie on your side with your elbow under your shoulder and feet stacked. Lift your hips so your body is a straight line and hold. Easier: bend your knees and balance on your bottom knee instead of your feet.",
            "Body weight."),
        ["Step-ups"] = new(
            "Face a sturdy step, box or bench, knee height or lower. Step up with one foot and push through that heel to stand tall on top, then step down slowly. Do all the reps on one leg, then switch.",
            "Start with no weight or 10 lb in each hand."),
        ["Dumbbell floor press"] = new(
            "Lie on the floor, knees bent, a dumbbell in each hand above your chest. Lower until your upper arms touch the floor, elbows at about 45 degrees from your body, then press back up.",
            "Start around 20 to 25 lb in each hand."),
        ["Biceps curl"] = new(
            "Stand with the dumbbells at your sides, palms forward. Curl them up while keeping your elbows by your sides and without swinging, then lower slowly.",
            "Start around 10 to 15 lb in each hand."),
        ["Triceps extension"] = new(
            "Hold one dumbbell with both hands straight overhead. Bend your elbows to lower it behind your head, keeping your elbows pointing up, then straighten your arms.",
            "Start around 15 lb."),
        ["Farmer carry"] = new(
            "Pick up a heavy dumbbell in each hand. Stand tall with your shoulders back and walk with short, steady steps for the time. Don't lean to either side.",
            "Start around 25 to 35 lb in each hand."),
        ["Front plank"] = new(
            "Forearms on the floor with your elbows under your shoulders. Body straight from heels to head; squeeze your glutes and tighten your stomach. Hold. Easier: drop to your knees.",
            "Body weight."),
    };

    public static ExerciseHowTo? For(string exercise) => Guides.GetValueOrDefault(exercise);

    static readonly HashSet<string> CoreReps = new(StringComparer.OrdinalIgnoreCase) { "Dead bug", "Bird dog" };

    /// <summary>Sets and reps (or seconds) for an exercise on a date, from the block week.</summary>
    public static string Prescription(ExerciseTemplate exercise, DateOnly date)
    {
        var week = WorkoutPlan.WeekInBlock(date);
        var sets = WorkoutPlan.TargetSets(date);
        var side = exercise.PerSide ? " each side" : "";
        if (exercise.Timed)
        {
            var seconds = exercise.Name == "Farmer carry" ? "30 to 40 s walking" : week == 1 ? "20 to 30 s" : "30 s";
            return $"{sets} sets of {seconds}{side}";
        }
        if (CoreReps.Contains(exercise.Name)) return $"{sets} sets of 8{side}";
        var reps = week switch { 1 or 2 => "10", 3 => "10 to 12", _ => "8 to 10" };
        return $"{sets} sets of {reps}{side}";
    }

    /// <summary>The whole session explained step by step, for the Today tab.</summary>
    public static string SessionText(string sessionType, DateOnly date)
    {
        if (sessionType == WorkoutPlan.Mobility) return WorkoutPlan.MobilityRoutineText;
        if (sessionType == WorkoutPlan.Run)
            return WorkoutPlan.RunTarget(date) + "\nEasy pace: you could hold a conversation. Walk 5 minutes first to warm up and 5 minutes after to cool down.";

        var exercises = WorkoutPlan.Exercises(sessionType, date);
        if (exercises.Count == 0) return "";
        var text = new StringBuilder();
        text.Append("Warm up 5 minutes: march in place, arm circles, 10 body-weight squats.\n");
        text.Append($"Then do each exercise in order. {Rest(date)}\n");
        var number = 1;
        foreach (var exercise in exercises)
        {
            var guide = For(exercise.Name);
            text.Append($"\n{number++}. {exercise.Name}: {Prescription(exercise, date)}\n");
            if (guide != null) text.Append($"How: {guide.How}\nWeight: {guide.Load}\n");
        }
        text.Append("\nPick a weight where the last 2 reps are hard but your form stays clean. Log your sets on the Fitness tab; next time it shows what you lifted.");
        return text.ToString();
    }

    static string Rest(DateOnly date) => WorkoutPlan.WeekInBlock(date) switch
    {
        3 => "Rest 60 to 90 seconds between sets.",
        4 => "Rest 90 seconds between sets.",
        _ => "Rest 60 seconds between sets.",
    };
}
