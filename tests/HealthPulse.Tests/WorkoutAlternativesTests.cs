using System.Text.Json;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public sealed class WorkoutAlternativesTests : IAsyncLifetime
{
    static readonly DateOnly Monday = new(2026, 11, 23);
    static readonly DateOnly Tuesday = new(2026, 11, 24);
    static readonly DateOnly Wednesday = new(2026, 11, 25);
    static readonly DateOnly Saturday = new(2026, 11, 28);

    readonly string path = Path.Combine(Path.GetTempPath(), $"alternatives-{Guid.NewGuid():N}.db3");
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

    [Fact]
    public void RunDays_OfferIndoorOptions_MatchedToTheDaysPlan()
    {
        var alternatives = WorkoutAlternatives.For(WorkoutPlan.Run, Tuesday);
        Assert.Equal(
            [WorkoutAlternatives.Treadmill, WorkoutAlternatives.Bike, WorkoutAlternatives.Stairs, WorkoutAlternatives.IndoorWalk],
            alternatives.Select(a => a.Name));
        var plan = WorkoutPlan.RunTarget(Tuesday);
        Assert.All(alternatives, a => Assert.Contains(plan, a.How));
    }

    [Fact]
    public void StrengthDays_OfferBodyWeightAndShortVersions()
    {
        var alternatives = WorkoutAlternatives.For(WorkoutPlan.StrengthA, Monday);
        Assert.Equal([WorkoutAlternatives.BodyWeight, WorkoutAlternatives.Short], alternatives.Select(a => a.Name));
        Assert.Contains("Goblet squat: Body-weight squat", alternatives[0].How);
        Assert.Contains("Push-ups: As planned.", alternatives[0].How);
        Assert.Contains("Goblet squat, Push-ups and Reverse lunge / split squat", alternatives[1].How);
    }

    [Fact]
    public void MobilityAndRestDays_HaveNoAlternatives()
    {
        Assert.Empty(WorkoutAlternatives.For(WorkoutPlan.Mobility, Monday.AddDays(6)));
        Assert.Empty(WorkoutAlternatives.For(null, Saturday));
    }

    [Fact]
    public void Swap_TradesTheTwoDaysPlans_AndUndoClearsBoth()
    {
        var swaps = WorkoutPlan.Swap(Tuesday, Saturday, []);
        Assert.Null(WorkoutPlan.PlannedFor(Tuesday, swaps));
        Assert.Equal(WorkoutPlan.Run, WorkoutPlan.PlannedFor(Saturday, swaps));
        Assert.Equal(WorkoutPlan.StrengthB, WorkoutPlan.PlannedFor(Wednesday, swaps));
        Assert.Equal("Swapped with Saturday (was Run).", WorkoutPlan.SwapNote(Tuesday, swaps));
        Assert.Equal(["2026-11-24", "2026-11-28"], WorkoutPlan.UndoSwap(Saturday, swaps));
    }

    [Fact]
    public void SwapOptions_AreOtherDaysThisWeekWithADifferentPlan()
    {
        var options = WorkoutPlan.SwapOptions(Tuesday, []);
        Assert.DoesNotContain(Tuesday, options);
        Assert.DoesNotContain(Tuesday.AddDays(2), options); // Thursday is also a run
        Assert.Contains(Saturday, options);
        Assert.Equal(5, options.Count);

        var swaps = WorkoutPlan.Swap(Tuesday, Wednesday, []);
        Assert.DoesNotContain(Wednesday, WorkoutPlan.SwapOptions(Monday, swaps));
    }

    [Fact]
    public void Swap_StaysWithinTheWeek() =>
        Assert.Throws<ArgumentException>(() => WorkoutPlan.Swap(Tuesday, Tuesday.AddDays(7), []));

    [Fact]
    public async Task Swaps_RoundTripThroughTheStore()
    {
        await store.SavePlanSwapsAsync(WorkoutPlan.Swap(Tuesday, Wednesday, []));
        var saved = await store.GetPlanSwapsAsync(Monday, Monday.AddDays(6));
        Assert.Equal(2, saved.Count);
        Assert.Equal(WorkoutPlan.StrengthB, WorkoutPlan.PlannedFor(Tuesday, saved));

        await store.DeletePlanSwapsAsync(WorkoutPlan.UndoSwap(Tuesday, saved));
        Assert.Empty(await store.GetPlanSwapsAsync(Monday, Monday.AddDays(6)));
    }

    [Fact]
    public void AlternativeRun_CountsTowardTheWeek()
    {
        var run = new WorkoutSession
        {
            Date = Tuesday.ToDateTime(new TimeOnly(7, 0)), SessionType = WorkoutPlan.Run,
            Alternative = WorkoutAlternatives.Treadmill,
        };
        Assert.Equal(1, WorkoutPlan.Progress(Monday, [run], []).RunsDone);
        Assert.Equal("Run (treadmill)", run.Summary);
    }

    [Fact]
    public void Reminders_FollowTheSwappedPlan_AndAlternativesCount()
    {
        var swaps = WorkoutPlan.Swap(Tuesday, Saturday, []);
        var rest = DailyReminders.OpenTasks(Tuesday, null, [], null, swaps);
        Assert.DoesNotContain(rest, t => t.Key == "workout");

        var saturday = DailyReminders.OpenTasks(Saturday, null, [], null, swaps);
        Assert.Contains(saturday, t => t.Key == "workout");

        var bike = new WorkoutSession
        {
            Date = Saturday.ToDateTime(new TimeOnly(9, 0)), SessionType = WorkoutPlan.Run, Alternative = WorkoutAlternatives.Bike,
        };
        Assert.DoesNotContain(DailyReminders.OpenTasks(Saturday, null, [bike], null, swaps), t => t.Key == "workout");
    }

    static ExerciseSummary Watch(string id, string type, DateTime start, double minutes) =>
        new() { Id = id, Type = type, Start = start, Minutes = minutes };

    [Fact]
    public async Task IndoorCardioOnARunDay_ImportsAsTheRun()
    {
        await WorkoutImporter.ImportAsync(store, [Watch("bike", "stationary_biking", Tuesday.ToDateTime(new TimeOnly(18, 0)), 25)]);
        var workout = Assert.Single(await store.GetWorkoutsAsync(Tuesday, Tuesday));
        Assert.Equal(WorkoutPlan.Run, workout.SessionType);
        Assert.Equal(WorkoutAlternatives.Bike, workout.Alternative);
    }

    [Fact]
    public async Task IndoorCardioOnAStrengthDay_StaysOther()
    {
        await WorkoutImporter.ImportAsync(store, [Watch("bike", "stationary_biking", Monday.ToDateTime(new TimeOnly(18, 0)), 25)]);
        var workout = Assert.Single(await store.GetWorkoutsAsync(Monday, Monday));
        Assert.Equal(WorkoutPlan.Other, workout.SessionType);
        Assert.Null(workout.Alternative);
    }

    [Fact]
    public async Task IndoorCardioOnASwappedInRunDay_ImportsAsTheRun()
    {
        await store.SavePlanSwapsAsync(WorkoutPlan.Swap(Tuesday, Saturday, []));
        await WorkoutImporter.ImportAsync(store, [Watch("stairs", "stair_climbing", Saturday.ToDateTime(new TimeOnly(10, 0)), 22)]);
        var workout = Assert.Single(await store.GetWorkoutsAsync(Saturday, Saturday));
        Assert.Equal(WorkoutPlan.Run, workout.SessionType);
        Assert.Equal(WorkoutAlternatives.Stairs, workout.Alternative);
    }

    [Fact]
    public async Task WatchBikeRide_LinksToAManuallyLoggedAlternativeRun()
    {
        await store.SaveWorkoutAsync(new WorkoutSession
        {
            Date = Tuesday.ToDateTime(new TimeOnly(18, 0)), SessionType = WorkoutPlan.Run, Alternative = WorkoutAlternatives.Bike,
        });
        var added = await WorkoutImporter.ImportAsync(store, [Watch("bike", "stationary_biking", Tuesday.ToDateTime(new TimeOnly(18, 5)), 25)]);
        Assert.Equal(0, added);
        var workout = Assert.Single(await store.GetWorkoutsAsync(Tuesday, Tuesday));
        Assert.Equal("bike", workout.ExternalId);
        Assert.Equal(WorkoutAlternatives.Bike, workout.Alternative);
    }

    [Fact]
    public void Export_IncludesTheAlternativeAndTheSwappedPlan()
    {
        var trackers = new TrackerExport([],
            [new WorkoutSession
            {
                Date = Saturday.ToDateTime(new TimeOnly(9, 0)), SessionType = WorkoutPlan.Run, Alternative = WorkoutAlternatives.Treadmill,
            }],
            [], [], [], [],
            WorkoutPlan.Swap(Tuesday, Saturday, []));
        var json = HealthExportService.BuildJson(DateTime.UtcNow, "America/Chicago", Tuesday, Saturday, [], [], trackers: trackers);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var days = root.GetProperty("days");
        Assert.Equal("Off", days[0].GetProperty("plannedSession").GetString());
        Assert.Equal("Run", days[4].GetProperty("plannedSession").GetString());
        Assert.Equal("Swapped with Tuesday (was Rest).", days[4].GetProperty("planSwap").GetString());
        Assert.Equal("Treadmill", root.GetProperty("workouts")[0].GetProperty("alternative").GetString());
    }
}
