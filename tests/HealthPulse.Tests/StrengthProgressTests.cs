using System.Text.Json;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public class StrengthProgressTests
{
    // Plan weeks: Nov 23 week 1, Nov 30 week 2, Dec 7 week 3, Dec 14 week 4, Dec 21 week 1 of block 2.
    static readonly ExerciseTemplate Squat = new("Goblet squat", Weighted: true);
    static readonly ExerciseTemplate Press = new("Half-kneeling shoulder press", Weighted: true, PerSide: true);
    static readonly ExerciseTemplate PushUps = new("Push-ups", Weighted: false);
    static readonly ExerciseTemplate Plank = new("Front plank", Weighted: false, Timed: true);

    static ExerciseSession Session(DateOnly date, double? weight, int[] reps, int?[]? rir = null, bool timed = false) =>
        new(date, reps.Select((r, i) => new WorkoutSet
        {
            Exercise = "x",
            Order = i,
            WeightLbs = weight,
            Reps = timed ? null : r,
            Seconds = timed ? r : null,
            RepsInReserve = rir?[i],
        }).ToList());

    [Fact]
    public void TopOfRangeWithRepsLeft_GoesUpOneStep()
    {
        var history = new[] { Session(new(2026, 12, 7), 15, [12, 12, 12], [2, 2, 3]) };
        var next = StrengthProgress.Recommend(Press, history, new DateOnly(2026, 12, 9));
        Assert.Equal(StrengthProgress.GoUpAction, next.Action);
        Assert.Equal(20, next.WeightLbs);
        Assert.True(next.GoUp);
        Assert.Equal("⬆ Go up to 20 lb", next.Tag);
        Assert.Equal(new RepRange(10, 12, false), next.Target);
    }

    [Fact]
    public void TopOfRangeButHard_StaysAtTheWeight()
    {
        var history = new[] { Session(new(2026, 12, 7), 15, [12, 12, 12], [1, 0, 1]) };
        var next = StrengthProgress.Recommend(Press, history, new DateOnly(2026, 12, 9));
        Assert.Equal(StrengthProgress.HoldAction, next.Action);
        Assert.Equal(15, next.WeightLbs);
    }

    [Fact]
    public void NoEffortLogged_GoesUpOnRepsAloneAndAsksForEffort()
    {
        var history = new[] { Session(new(2026, 12, 7), 15, [12, 12, 12]) };
        var next = StrengthProgress.Recommend(Press, history, new DateOnly(2026, 12, 9));
        Assert.Equal(StrengthProgress.GoUpAction, next.Action);
        Assert.Contains("reps left", next.Reason);
    }

    [Fact]
    public void BelowTheTop_Holds()
    {
        var history = new[] { Session(new(2026, 12, 7), 15, [12, 11, 10], [2, 1, 1]) };
        var next = StrengthProgress.Recommend(Press, history, new DateOnly(2026, 12, 9));
        Assert.Equal(StrengthProgress.HoldAction, next.Action);
        Assert.Equal(15, next.WeightLbs);
    }

    [Fact]
    public void WeekTwo_KeepsWeekOneWeightsUnlessVeryEasy()
    {
        var easy = new[] { Session(new(2026, 11, 23), 15, [10, 10], [2, 2]) };
        Assert.Equal(StrengthProgress.HoldAction, StrengthProgress.Recommend(Press, easy, new DateOnly(2026, 11, 30)).Action);

        var tooLight = new[] { Session(new(2026, 11, 23), 15, [10, 10], [3, 3]) };
        var next = StrengthProgress.Recommend(Press, tooLight, new DateOnly(2026, 11, 30));
        Assert.Equal(StrengthProgress.GoUpAction, next.Action);
        Assert.Equal(20, next.WeightLbs);
    }

    [Fact]
    public void WeekFour_GoesHeavierAfterWeekThree()
    {
        var history = new[] { Session(new(2026, 12, 11), 15, [11, 10, 10], [1, 1, 1]) };
        var next = StrengthProgress.Recommend(Press, history, new DateOnly(2026, 12, 14));
        Assert.Equal(StrengthProgress.GoUpAction, next.Action);
        Assert.Equal(20, next.WeightLbs);
        Assert.Equal(new RepRange(8, 10, false), next.Target);
    }

    [Fact]
    public void MissedReps_RepeatsThenBacksOff()
    {
        var once = new[] { Session(new(2026, 12, 7), 20, [8, 7, 6], [0, 0, 0]) };
        var first = StrengthProgress.Recommend(Press, once, new DateOnly(2026, 12, 9));
        Assert.Equal(StrengthProgress.RepeatAction, first.Action);
        Assert.Equal(20, first.WeightLbs);

        var twice = new[] { once[0], Session(new(2026, 12, 9), 20, [8, 8, 7], [0, 0, 0]) };
        var second = StrengthProgress.Recommend(Press, twice, new DateOnly(2026, 12, 11));
        Assert.Equal(StrengthProgress.BackOffAction, second.Action);
        Assert.Equal(15, second.WeightLbs);
        Assert.True(second.BackOff);
    }

    [Fact]
    public void NewBlock_StartsALittleLighter()
    {
        var history = new[] { Session(new(2026, 12, 14), 35, [10, 9, 8], [1, 1, 0]) };
        var next = StrengthProgress.Recommend(Squat, history, new DateOnly(2026, 12, 21));
        Assert.Equal(StrengthProgress.HoldAction, next.Action);
        Assert.Equal(25, next.WeightLbs);
    }

    [Fact]
    public void GobletSquat_JumpsTenOnceHeavy()
    {
        Assert.Equal(5, StrengthProgress.Step("Goblet squat", 25));
        Assert.Equal(10, StrengthProgress.Step("Goblet squat", 30));
        Assert.Equal(5, StrengthProgress.Step("Biceps curl", 30));
        var history = new[] { Session(new(2026, 12, 7), 30, [12, 12, 12], [2, 2, 2]) };
        Assert.Equal(40, StrengthProgress.Recommend(Squat, history, new DateOnly(2026, 12, 9)).WeightLbs);
    }

    [Fact]
    public void BodyWeightAddsReps_TimedAddsSeconds()
    {
        var push = StrengthProgress.Recommend(PushUps, [Session(new(2026, 12, 7), null, [12, 12, 12], [2, 2, 2])], new DateOnly(2026, 12, 9));
        Assert.Equal(StrengthProgress.AddRepsAction, push.Action);
        Assert.Equal(new RepRange(13, 14, false), push.Target);
        Assert.Null(push.WeightLbs);

        var plank = StrengthProgress.Recommend(Plank, [Session(new(2026, 12, 7), null, [30, 30], [2, 2], timed: true)], new DateOnly(2026, 12, 9));
        Assert.Equal(StrengthProgress.AddTimeAction, plank.Action);
        Assert.Equal(new RepRange(35, 40, true), plank.Target);
    }

    [Fact]
    public void FirstTime_SuggestsAStartingWeight()
    {
        var next = StrengthProgress.Recommend(Squat, [], new DateOnly(2026, 11, 23));
        Assert.Equal(StrengthProgress.StartAction, next.Action);
        Assert.Contains("20 to 25 lb", next.Reason);
    }

    [Fact]
    public void E1rm_CountsRepsLeft()
    {
        Assert.Equal(35, StrengthProgress.E1rm(new WorkoutSet { WeightLbs = 25, Reps = 10, RepsInReserve = 2 }));
        Assert.Null(StrengthProgress.E1rm(new WorkoutSet { Reps = 10 }));
    }

    static List<WorkoutSession> Workouts() =>
    [
        new()
        {
            Date = new DateTime(2026, 12, 7, 7, 0, 0), SessionType = WorkoutPlan.StrengthA,
            Sets =
            [
                new() { Exercise = "Goblet squat", Reps = 12, WeightLbs = 25, RepsInReserve = 2, Order = 0 },
                new() { Exercise = "Goblet squat", Reps = 12, WeightLbs = 25, RepsInReserve = 3, Order = 1 },
                new() { Exercise = "Push-ups", Reps = 9, Order = 2 },
            ]
        },
        new()
        {
            Date = new DateTime(2026, 11, 30, 7, 0, 0), SessionType = WorkoutPlan.StrengthA,
            Sets = [new() { Exercise = "Goblet squat", Reps = 10, WeightLbs = 20, RepsInReserve = 2, Order = 0 }]
        },
    ];

    [Fact]
    public void Summaries_TrackEachExerciseAndTheNextSession()
    {
        var progress = StrengthProgress.Summaries(Workouts(), new DateOnly(2026, 12, 8));
        Assert.Equal(new[] { "Goblet squat", "Push-ups" }, progress.Select(p => p.Exercise.Name));

        var squat = progress[0];
        Assert.Equal(2, squat.Sessions.Count);
        Assert.Equal(20, squat.First.WeightLbs);
        Assert.Equal(25, squat.Last.WeightLbs);
        Assert.Equal(new DateOnly(2026, 12, 14), squat.NextDate); // next Monday, Strength A
        Assert.Equal(StrengthProgress.GoUpAction, squat.Next.Action);
        Assert.Equal(30, squat.Next.WeightLbs);
        Assert.Equal("Goblet squat: 20 → 25 lb since Nov 30 • ⬆ Go up to 30 lb", squat.Text);
        Assert.Equal(25 * 12 * 2, squat.Last.VolumeLbs);
    }

    [Fact]
    public void Export_IncludesEffortAndStrengthProgress()
    {
        var json = HealthExportService.BuildJson(DateTime.UtcNow, "America/Chicago",
            new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 8), [], [],
            trackers: new TrackerExport([], Workouts(), [], [], []));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var workout = root.GetProperty("workouts")[1];
        Assert.Equal(2, workout.GetProperty("sets")[0].GetProperty("rir").GetInt32());
        Assert.False(workout.GetProperty("sets")[2].TryGetProperty("rir", out _));
        Assert.Equal(600, workout.GetProperty("volumeLbs").GetDouble());

        var strength = root.GetProperty("strength");
        Assert.Contains("reps in reserve", strength.GetProperty("rule").GetString(), StringComparison.OrdinalIgnoreCase);
        var squat = strength.GetProperty("exercises")[0];
        Assert.Equal("Goblet squat", squat.GetProperty("exercise").GetString());
        Assert.Equal(5, squat.GetProperty("weightChangeLbs").GetDouble());
        Assert.Equal(2, squat.GetProperty("history").GetArrayLength());
        Assert.Equal(3, squat.GetProperty("history")[1].GetProperty("rir")[1].GetInt32());
        var next = squat.GetProperty("next");
        Assert.Equal("go_up", next.GetProperty("action").GetString());
        Assert.Equal(30, next.GetProperty("weightLbs").GetDouble());
        Assert.Equal("2026-12-14", next.GetProperty("date").GetString());
    }

    [Fact]
    public void Export_LeavesStrengthOutWithoutSets()
    {
        var json = HealthExportService.BuildJson(DateTime.UtcNow, "UTC", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 1), [], []);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("strength", out _));
    }

    [Fact]
    public void SetsKeepRepsInReserve()
    {
        var sets = WorkoutForm.BuildSets(
        [
            new SetInput("Goblet squat", false, "10", "25", 2),
            new SetInput("Goblet squat", false, "10", "25", 5),
            new SetInput("Goblet squat", false, "9", "25"),
        ]);
        Assert.Equal(new int?[] { 2, 3, null }, sets.Select(s => s.RepsInReserve));
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new WorkoutSession
        {
            SessionType = WorkoutPlan.StrengthA,
            Sets = [new WorkoutSet { Exercise = "Goblet squat", Reps = 10, RepsInReserve = 4 }]
        }));
    }

    [Fact]
    public void LastTimeText_ShowsEachSetAndEffort()
    {
        var sets = new List<WorkoutSet>
        {
            new() { Exercise = "Goblet squat", Reps = 12, WeightLbs = 25, RepsInReserve = 2 },
            new() { Exercise = "Goblet squat", Reps = 10, WeightLbs = 25, RepsInReserve = 1 },
        };
        Assert.Equal("Last time: 2 x 12/10 @ 25 lb (1 left on the hardest set)", WorkoutPlan.LastTimeText(sets));
    }
}
