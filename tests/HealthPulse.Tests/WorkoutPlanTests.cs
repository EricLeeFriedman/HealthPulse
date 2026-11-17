using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public class WorkoutPlanTests
{
    [Theory]
    [InlineData(DayOfWeek.Monday, "Strength A")]
    [InlineData(DayOfWeek.Tuesday, "Run")]
    [InlineData(DayOfWeek.Wednesday, "Strength B")]
    [InlineData(DayOfWeek.Thursday, "Run")]
    [InlineData(DayOfWeek.Friday, "Strength C")]
    [InlineData(DayOfWeek.Sunday, "Mobility")]
    public void PlannedFor_MatchesWeeklySchedule(DayOfWeek day, string expected) =>
        Assert.Equal(expected, WorkoutPlan.PlannedFor(day));

    [Fact]
    public void Saturday_IsOff() => Assert.Null(WorkoutPlan.PlannedFor(DayOfWeek.Saturday));

    [Theory]
    [InlineData("2026-11-17", 1, 1)] // before the plan starts
    [InlineData("2026-11-23", 1, 1)]
    [InlineData("2026-11-29", 1, 1)] // Sunday still belongs to week 1
    [InlineData("2026-11-30", 1, 2)]
    [InlineData("2026-12-09", 1, 3)]
    [InlineData("2026-12-14", 1, 4)]
    [InlineData("2026-12-21", 2, 1)]
    [InlineData("2027-02-08", 3, 4)]
    public void BlockAndWeek_FollowFourWeekCycles(string date, int block, int week)
    {
        var day = DateOnly.Parse(date);
        Assert.Equal(block, WorkoutPlan.Block(day));
        Assert.Equal(week, WorkoutPlan.WeekInBlock(day));
    }

    [Fact]
    public void WeekStart_IsMonday()
    {
        Assert.Equal(new DateOnly(2026, 11, 23), WorkoutPlan.WeekStart(new DateOnly(2026, 11, 29)));
        Assert.Equal(new DateOnly(2026, 11, 23), WorkoutPlan.WeekStart(new DateOnly(2026, 11, 23)));
    }

    [Fact]
    public void Targets_FollowTheProgression()
    {
        Assert.Equal(2, WorkoutPlan.TargetSets(new DateOnly(2026, 11, 23)));
        Assert.Equal(3, WorkoutPlan.TargetSets(new DateOnly(2026, 11, 30)));
        Assert.StartsWith("Run 3 min, walk 1 min", WorkoutPlan.RunTarget(new DateOnly(2026, 11, 24)));
        Assert.StartsWith("Tue: 20 min nonstop", WorkoutPlan.RunTarget(new DateOnly(2026, 12, 15)));
        Assert.Contains("20 to 30 minutes nonstop", WorkoutPlan.RunTarget(new DateOnly(2026, 12, 22)));
        Assert.Contains("Child's pose", WorkoutPlan.TargetFor(WorkoutPlan.Mobility, new DateOnly(2026, 11, 29)));
    }

    [Fact]
    public void StrengthB_SwapsGluteBridgeForRomanianDeadliftInWeek3()
    {
        var week1 = WorkoutPlan.Exercises(WorkoutPlan.StrengthB, new DateOnly(2026, 11, 25)).Select(e => e.Name).ToList();
        var week3 = WorkoutPlan.Exercises(WorkoutPlan.StrengthB, new DateOnly(2026, 12, 9)).Select(e => e.Name).ToList();
        Assert.Contains("Glute bridge", week1);
        Assert.DoesNotContain("Dumbbell Romanian deadlift", week1);
        Assert.Contains("Dumbbell Romanian deadlift", week3);
        Assert.Empty(WorkoutPlan.Exercises(WorkoutPlan.Run, new DateOnly(2026, 12, 9)));
    }

    [Fact]
    public void StrengthSessions_ListThePlanExercises()
    {
        var day = new DateOnly(2026, 11, 23);
        Assert.Equal(5, WorkoutPlan.Exercises(WorkoutPlan.StrengthA, day).Count);
        var c = WorkoutPlan.Exercises(WorkoutPlan.StrengthC, day);
        Assert.Equal(7, c.Count);
        var carry = c.Single(e => e.Name == "Farmer carry");
        Assert.True(carry.Timed);
        Assert.True(carry.Weighted);
        Assert.Equal("seconds", carry.Unit);
        Assert.Equal("reps each side", c.Single(e => e.Name == "Step-ups").Unit);
    }

    [Fact]
    public void Progress_CountsSessionsAndMobilityFromCheckIn()
    {
        var monday = new DateOnly(2026, 11, 23);
        var sessions = new List<WorkoutSession>
        {
            new() { Date = new DateTime(2026, 11, 23, 7, 0, 0), SessionType = WorkoutPlan.StrengthA },
            new() { Date = new DateTime(2026, 11, 24, 7, 0, 0), SessionType = WorkoutPlan.Run },
            new() { Date = new DateTime(2026, 11, 25, 7, 0, 0), SessionType = WorkoutPlan.StrengthB },
            new() { Date = new DateTime(2026, 11, 26, 7, 0, 0), SessionType = WorkoutPlan.Run },
            new() { Date = new DateTime(2026, 11, 27, 7, 0, 0), SessionType = WorkoutPlan.Run }, // extra run
            new() { Date = new DateTime(2026, 11, 30, 7, 0, 0), SessionType = WorkoutPlan.StrengthC }, // next week
        };
        var checkIns = new List<DailyCheckIn> { new() { Date = "2026-11-29", MobilityDone = true } };

        var progress = WorkoutPlan.Progress(monday, sessions, checkIns);

        Assert.Equal(2, progress.StrengthDone);
        Assert.Equal(3, progress.RunsDone);
        Assert.True(progress.MobilityDone);
        Assert.Equal(5, progress.Done); // runs cap at 2
        Assert.StartsWith("5 of 6 sessions this week", progress.Text);
        Assert.False(WorkoutPlan.Progress(monday, [], []).MobilityDone);
    }

    [Fact]
    public void LastSets_AndGoUpRule()
    {
        var history = new List<WorkoutSession>
        {
            new() { Date = new DateTime(2026, 12, 7), Sets = [
                new() { Exercise = "Goblet squat", Reps = 10, WeightLbs = 25, Order = 0 }] },
            new() { Date = new DateTime(2026, 12, 9), Sets = [
                new() { Exercise = "Goblet squat", Reps = 12, WeightLbs = 25, Order = 0 },
                new() { Exercise = "Goblet squat", Reps = 12, WeightLbs = 25, Order = 1 },
                new() { Exercise = "Push-ups", Reps = 12, Order = 2 }] },
        };

        var last = WorkoutPlan.LastSets("Goblet squat", history);
        Assert.Equal(2, last.Count);
        Assert.Equal("Last time: 2 x 12 @ 25 lb", WorkoutPlan.LastTimeText(last));
        Assert.True(WorkoutPlan.ShouldGoUp(last, new DateOnly(2026, 12, 11)));   // week 3
        Assert.False(WorkoutPlan.ShouldGoUp(last, new DateOnly(2026, 11, 25)));  // week 1
        Assert.False(WorkoutPlan.ShouldGoUp(WorkoutPlan.LastSets("Push-ups", history), new DateOnly(2026, 12, 11))); // bodyweight
        Assert.Empty(WorkoutPlan.LastSets("Step-ups", history));
        Assert.Equal("", WorkoutPlan.LastTimeText([]));
    }
}
