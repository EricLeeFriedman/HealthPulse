using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public sealed class WorkoutImporterTests : IAsyncLifetime
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"import-{Guid.NewGuid():N}.db3");
    LocalTrackerStore store = null!;

    public Task InitializeAsync()
    {
        store = new LocalTrackerStore(path);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await store.Database.CloseAsync();
        File.Delete(path);
    }

    static ExerciseSummary Session(string id, string type, DateTime start, double minutes,
        double? miles = null, double? avgHr = null) =>
        new() { Id = id, Type = type, Start = start, Minutes = minutes, DistanceMiles = miles, AvgHeartRate = avgHr };

    static readonly DateOnly Week = new(2026, 11, 23);
    static readonly DateOnly WeekEnd = new(2026, 11, 29);

    [Theory]
    [InlineData("running", "Run")]
    [InlineData("running_treadmill", "Run")]
    [InlineData("strength_training", "strength")]
    [InlineData("weightlifting", "strength")]
    [InlineData("calisthenics", "strength")]
    [InlineData("stretching", "Mobility")]
    [InlineData("yoga", "Mobility")]
    [InlineData("biking", "Other")]
    [InlineData("hiit", "Other")]
    public void CategoryFor_MapsWatchTypesToThePlan(string type, string expected) =>
        Assert.Equal(expected, WorkoutImporter.CategoryFor(type));

    [Theory]
    [InlineData("walking")]
    [InlineData("hiking")]
    public void Walks_AreNotPlanSessions(string type) => Assert.Null(WorkoutImporter.CategoryFor(type));

    [Fact]
    public void ShortSessions_AreSkipped()
    {
        Assert.False(WorkoutImporter.IsLongEnough("Run", 6));
        Assert.True(WorkoutImporter.IsLongEnough("Run", 12));
        Assert.True(WorkoutImporter.IsLongEnough("Mobility", 6));
        Assert.False(WorkoutImporter.IsLongEnough("Other", 10));
    }

    [Fact]
    public void StrengthSession_UsesTheDaysPlanOrTheNextOneNotDone()
    {
        Assert.Equal("Strength B", WorkoutImporter.StrengthSessionFor(new DateTime(2026, 11, 25, 7, 0, 0), []));
        var done = new List<WorkoutSession> { new() { Date = new DateTime(2026, 11, 23, 7, 0, 0), SessionType = "Strength A" } };
        // Tuesday is a run day: the first strength session not yet done this week is B.
        Assert.Equal("Strength B", WorkoutImporter.StrengthSessionFor(new DateTime(2026, 11, 24, 18, 0, 0), done));
        Assert.Equal("Strength A", WorkoutImporter.StrengthSessionFor(new DateTime(2026, 11, 24, 18, 0, 0), []));
    }

    [Fact]
    public async Task Import_CreatesPlanWorkoutsAndCountsThemTowardTheWeek()
    {
        var added = await WorkoutImporter.ImportAsync(store,
        [
            Session("hc-1", "strength_training", new DateTime(2026, 11, 23, 7, 0, 0), 31, avgHr: 112),
            Session("hc-2", "running", new DateTime(2026, 11, 24, 7, 0, 0), 21.6, miles: 1.734, avgHr: 148),
            Session("hc-3", "walking", new DateTime(2026, 11, 24, 18, 0, 0), 40),   // dog walk
            Session("hc-4", "running", new DateTime(2026, 11, 26, 7, 0, 0), 4),     // too short
            Session("hc-5", "stretching", new DateTime(2026, 11, 29, 9, 0, 0), 15),
            new ExerciseSummary { Type = "running", Start = new DateTime(2026, 11, 26, 8, 0, 0), Minutes = 25 }, // no id
        ]);

        Assert.Equal(3, added);
        var workouts = await store.GetWorkoutsAsync(Week, WeekEnd);
        Assert.Equal(new[] { "Strength A", "Run", "Mobility" }, workouts.Select(w => w.SessionType));
        var run = workouts[1];
        Assert.True(run.IsDetected);
        Assert.Equal(22, run.DurationMinutes);
        Assert.Equal(1.73, run.DistanceMiles);
        Assert.Equal(148, run.AvgHeartRate);
        Assert.Equal("running", run.ExerciseType);
        Assert.Equal(3, WorkoutPlan.Progress(Week, workouts, []).Done);
    }

    [Fact]
    public async Task Import_IsIdempotentAndRefreshesMeasurements()
    {
        await WorkoutImporter.ImportAsync(store, [Session("hc-1", "running", new DateTime(2026, 11, 24, 7, 0, 0), 20)]);
        var added = await WorkoutImporter.ImportAsync(store,
            [Session("hc-1", "running", new DateTime(2026, 11, 24, 7, 0, 0), 20, miles: 1.8, avgHr: 150)]);

        Assert.Equal(0, added);
        var run = Assert.Single(await store.GetWorkoutsAsync(Week, WeekEnd));
        Assert.Equal(1.8, run.DistanceMiles);
        Assert.Equal(150, run.AvgHeartRate);
    }

    [Fact]
    public async Task Import_KeepsUserEditsAndOptionalSets()
    {
        await WorkoutImporter.ImportAsync(store, [Session("hc-1", "strength_training", new DateTime(2026, 11, 24, 18, 0, 0), 30)]);
        var workout = Assert.Single(await store.GetWorkoutsAsync(Week, WeekEnd));
        Assert.Equal("Strength A", workout.SessionType);
        workout.SessionType = "Strength C";
        workout.Sets = [new WorkoutSet { Exercise = "Goblet squat", Reps = 10, WeightLbs = 25 }];
        await store.SaveWorkoutAsync(workout);

        await WorkoutImporter.ImportAsync(store, [Session("hc-1", "strength_training", new DateTime(2026, 11, 24, 18, 0, 0), 32, avgHr: 105)]);

        var again = Assert.Single(await store.GetWorkoutsAsync(Week, WeekEnd));
        Assert.Equal("Strength C", again.SessionType);
        Assert.Single(again.Sets);
        Assert.Equal(32, again.DurationMinutes);
    }

    [Fact]
    public async Task DeletedDetectedWorkout_IsNotImportedAgain()
    {
        await WorkoutImporter.ImportAsync(store, [Session("hc-1", "running", new DateTime(2026, 11, 24, 7, 0, 0), 20)]);
        var run = Assert.Single(await store.GetWorkoutsAsync(Week, WeekEnd));

        await store.DeleteWorkoutAsync(run.Id);
        var added = await WorkoutImporter.ImportAsync(store, [Session("hc-1", "running", new DateTime(2026, 11, 24, 7, 0, 0), 20)]);

        Assert.Equal(0, added);
        Assert.Empty(await store.GetWorkoutsAsync(Week, WeekEnd));
        Assert.True(Assert.Single(await store.GetAllWorkoutsAsync(Week, WeekEnd)).Hidden);
    }

    [Fact]
    public async Task ManualWorkout_SameDayAndKind_IsLinkedNotDuplicated()
    {
        await store.SaveWorkoutAsync(new WorkoutSession
        {
            Date = new DateTime(2026, 11, 23, 7, 0, 0), SessionType = "Strength A",
            Sets = [new WorkoutSet { Exercise = "Push-ups", Reps = 12 }]
        });

        var added = await WorkoutImporter.ImportAsync(store,
            [Session("hc-9", "weightlifting", new DateTime(2026, 11, 23, 7, 5, 0), 28, avgHr: 110)]);

        Assert.Equal(0, added);
        var workout = Assert.Single(await store.GetWorkoutsAsync(Week, WeekEnd));
        Assert.Equal("hc-9", workout.ExternalId);
        Assert.False(workout.IsDetected); // still the manual entry, now linked
        Assert.Equal(28, workout.DurationMinutes);
        Assert.Equal(110, workout.AvgHeartRate);
        Assert.Single(workout.Sets);
    }

    [Fact]
    public void HealthSummaryBuilder_KeepsTheRecordId()
    {
        var days = HealthSummaryBuilder.CreateDays(new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 24));
        var summary = HealthSummaryBuilder.AddExercise(days, "running", null,
            new DateTime(2026, 11, 24, 7, 0, 0), new DateTime(2026, 11, 24, 7, 25, 0), "abc-123");
        Assert.NotNull(summary);
        Assert.Equal("abc-123", summary.Id);
        Assert.Equal(25, summary.Minutes);
        Assert.Null(HealthSummaryBuilder.AddExercise(days, "running", null,
            new DateTime(2026, 11, 30, 7, 0, 0), new DateTime(2026, 11, 30, 7, 25, 0), "out-of-range"));
    }
}
