using HealthPulse.Services;

namespace HealthPulse.Tests;

public class WorkoutFormTests
{
    [Fact]
    public void BuildSets_KeepsDoneSetsAndSkipsUntouchedRows()
    {
        var sets = WorkoutForm.BuildSets(
        [
            new SetInput("Goblet squat", false, "10", "25"),
            new SetInput("Goblet squat", false, "9", "25"),
            new SetInput("Goblet squat", false, "", "25"), // weight prefilled, set not done
            new SetInput("Push-ups", false, "12", ""),
            new SetInput("Front plank", true, "45", null),
        ]);

        Assert.Equal(4, sets.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, sets.Select(s => s.Order));
        Assert.Equal(25, sets[0].WeightLbs);
        Assert.Equal(9, sets[1].Reps);
        Assert.Null(sets[2].WeightLbs);
        Assert.Equal(12, sets[2].Reps);
        Assert.Equal(45, sets[3].Seconds);
        Assert.Null(sets[3].Reps);
    }

    [Fact]
    public void BuildSets_RejectsNonNumbers()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            WorkoutForm.BuildSets([new SetInput("Goblet squat", false, "ten", "25")]));
        Assert.Contains("Goblet squat", error.Message);
    }
}
