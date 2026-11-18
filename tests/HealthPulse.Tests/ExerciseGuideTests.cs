using HealthPulse.Services;

namespace HealthPulse.Tests;

public class ExerciseGuideTests
{
    static readonly DateOnly Week1 = new(2026, 11, 25);  // Wed, block 1 week 1
    static readonly DateOnly Week3 = new(2026, 12, 9);   // Wed, block 1 week 3
    static readonly DateOnly Week4 = new(2026, 12, 16);

    [Fact]
    public void EveryPlannedExerciseHasInstructions()
    {
        foreach (var date in new[] { Week1, Week3 })
        foreach (var session in new[] { WorkoutPlan.StrengthA, WorkoutPlan.StrengthB, WorkoutPlan.StrengthC })
        foreach (var exercise in WorkoutPlan.Exercises(session, date))
        {
            var guide = ExerciseGuide.For(exercise.Name);
            Assert.True(guide != null, $"No guide for {exercise.Name}");
            Assert.False(string.IsNullOrWhiteSpace(guide!.How));
            Assert.False(string.IsNullOrWhiteSpace(guide.Load));
        }
    }

    [Fact]
    public void Prescription_FollowsTheBlockWeek()
    {
        var row = new ExerciseTemplate("One-arm dumbbell row", Weighted: true, PerSide: true);
        Assert.Equal("2 sets of 10 each side", ExerciseGuide.Prescription(row, Week1));
        Assert.Equal("3 sets of 10 to 12 each side", ExerciseGuide.Prescription(row, Week3));
        Assert.Equal("3 sets of 8 to 10 each side", ExerciseGuide.Prescription(row, Week4));
        Assert.Equal("2 sets of 20 to 30 s each side",
            ExerciseGuide.Prescription(new ExerciseTemplate("Side plank", false, Timed: true, PerSide: true), Week1));
        Assert.Equal("3 sets of 8 each side",
            ExerciseGuide.Prescription(new ExerciseTemplate("Bird dog", false, PerSide: true), Week3));
        Assert.Equal("3 sets of 30 to 40 s walking",
            ExerciseGuide.Prescription(new ExerciseTemplate("Farmer carry", true, Timed: true), Week3));
    }

    [Fact]
    public void SessionText_WalksThroughStrengthB()
    {
        var text = ExerciseGuide.SessionText(WorkoutPlan.StrengthB, Week1);

        Assert.StartsWith("Warm up 5 minutes", text);
        Assert.Contains("Rest 60 seconds between sets.", text);
        Assert.Contains("1. One-arm dumbbell row: 2 sets of 10 each side", text);
        Assert.Contains("2. Glute bridge: 2 sets of 10", text);
        Assert.Contains("5. Side plank: 2 sets of 20 to 30 s each side", text);
        Assert.Contains("How: ", text);
        Assert.Contains("Weight: ", text);
        Assert.Contains("2. Dumbbell Romanian deadlift", ExerciseGuide.SessionText(WorkoutPlan.StrengthB, Week3));
    }

    [Fact]
    public void SessionText_RunAndMobility()
    {
        Assert.Contains("Easy pace", ExerciseGuide.SessionText(WorkoutPlan.Run, Week1));
        Assert.Equal(WorkoutPlan.MobilityRoutineText, ExerciseGuide.SessionText(WorkoutPlan.Mobility, Week1));
    }
}
