using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.ViewModels;

public partial class FitnessViewModel : ObservableObject
{
    public ITrackerStore Store;
    public IHealthDataService HealthData;
    public ErrorLog Errors;
    DateTime lastSync = DateTime.MinValue;

    public FitnessViewModel(ITrackerStore store, IHealthDataService healthData, ErrorLog errors)
    {
        Store = store;
        HealthData = healthData;
        Errors = errors;
    }

    [ObservableProperty] public partial string SyncText { get; set; } = "";
    [ObservableProperty] public partial bool IsSyncing { get; set; }

    /// <summary>Imports the last two weeks of watch workouts from Health Connect.</summary>
    public async Task SyncAsync(bool force = false)
    {
        if (IsSyncing || (!force && DateTime.Now - lastSync < TimeSpan.FromMinutes(5))) return;
        IsSyncing = true;
        try
        {
            if (HealthData.GetAvailability() != HealthDataAvailability.Available || !await HealthData.HasPermissionsAsync())
            {
                SyncText = "Connect Health Connect on the Health tab so workouts from your watch show up here automatically.";
                return;
            }
            var today = DateOnly.FromDateTime(DateTime.Today);
            var days = await HealthData.GetDailySummariesAsync(today.AddDays(-13), today);
            var added = await WorkoutImporter.ImportAsync(Store, days.SelectMany(d => d.Exercise));
            lastSync = DateTime.Now;
            SyncText = added > 0
                ? $"Added {added} workout{(added == 1 ? "" : "s")} from your watch. Tap one to add sets and weights (optional)."
                : $"Workouts from your watch sync automatically. Last checked {DateTime.Now:h:mm tt}.";
        }
        catch (Exception exception)
        {
            Errors.Record("Syncing watch workouts", exception);
            SyncText = $"Couldn't read workouts from Health Connect: {exception.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    async Task SyncNowAsync()
    {
        await SyncAsync(force: true);
        await LoadAsync();
    }

    [ObservableProperty] public partial string WeekText { get; set; } = "";
    [ObservableProperty] public partial string TodayText { get; set; } = "";
    [ObservableProperty] public partial string TargetText { get; set; } = "";
    [ObservableProperty] public partial string BodyText { get; set; } = "";
    [ObservableProperty] public partial string BodyDueText { get; set; } = "";
    [ObservableProperty] public partial string TestText { get; set; } = "";
    [ObservableProperty] public partial string TestDueText { get; set; } = "";
    [ObservableProperty] public partial ObservableCollection<WorkoutSession> Workouts { get; set; } = [];
    [ObservableProperty] public partial string LiftingText { get; set; } = "";
    [ObservableProperty] public partial string LiftingSummary { get; set; } = "";

    public async Task LoadAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var weekStart = WorkoutPlan.WeekStart(today);
            var workouts = await Store.GetWorkoutsAsync(today.AddDays(-60), today);
            var checkIns = await Store.GetCheckInsAsync(weekStart, today);
            WeekText = WorkoutPlan.Progress(weekStart, workouts, checkIns).Text;

            var swaps = await Store.GetPlanSwapsAsync(today, today);
            var planned = WorkoutPlan.PlannedFor(today, swaps);
            var doneToday = workouts.Any(w => DateOnly.FromDateTime(w.Date) == today);
            TodayText = planned == null
                ? $"Today: rest.{(WorkoutPlan.IsSwapped(today, swaps) ? " " + WorkoutPlan.SwapNote(today, swaps) : "")}"
                : $"Today: {planned}{(doneToday ? " ✓ logged" : "")} • block {WorkoutPlan.Block(today)}, week {WorkoutPlan.WeekInBlock(today)}";
            TargetText = planned == null ? "" : WorkoutPlan.TargetFor(planned, today);
            Workouts = new ObservableCollection<WorkoutSession>(workouts.OrderByDescending(w => w.Date));

            var lifting = StrengthProgress.Summaries(await Store.GetWorkoutsAsync(today.AddDays(-120), today), today);
            LiftingText = lifting.Count == 0
                ? "Log weights, reps and reps left on a strength day and each exercise shows here with when to go up."
                : string.Join("\n", lifting.Select(p => p.Text));
            var ready = lifting.Count(p => p.Next.GoUp);
            var stalled = lifting.Count(p => p.StalledSessions >= 3 && p.Next.Action == StrengthProgress.HoldAction);
            LiftingSummary = lifting.Count == 0 ? "" : string.Join(" ", new[]
            {
                ready > 0 ? $"{ready} exercise{(ready == 1 ? " is" : "s are")} ready to go up next session." : "Nothing to increase yet; keep building reps.",
                stalled > 0 ? $"{stalled} stuck at the same weight for 3+ sessions." : null,
            }.Where(t => t != null));

            var measurements = await Store.GetMeasurementsAsync(today.AddDays(-400), today);
            var lastWaist = measurements.LastOrDefault(m => m.WaistInches.HasValue);
            var lastWeight = measurements.LastOrDefault(m => m.WeightLbs.HasValue);
            var firstWaist = measurements.FirstOrDefault(m => m.WaistInches.HasValue);
            var parts = new List<string>();
            if (lastWaist != null)
            {
                var change = firstWaist != null && firstWaist != lastWaist
                    ? $" ({lastWaist.WaistInches - firstWaist.WaistInches:+0.0;-0.0;0} in since {firstWaist.Date:MMM d})"
                    : "";
                parts.Add($"Waist {lastWaist.WaistInches:0.0} in on {lastWaist.Date:MMM d}{change}");
            }
            if (lastWeight != null) parts.Add($"Weight {lastWeight.WeightLbs:0.#} lb on {lastWeight.Date:MMM d}");
            BodyText = parts.Count > 0 ? string.Join("\n", parts) : "No measurements yet.";

            var due = new List<string>();
            if (TrackerRules.IsWeeklyMeasurementDue(lastWaist == null ? null : DateOnly.FromDateTime(lastWaist.Date), today))
                due.Add("Waist and weight are due this week.");
            var lastPhoto = measurements.LastOrDefault(m => m.PhotoList.Count > 0);
            if (TrackerRules.IsMonthlyDue(lastPhoto == null ? null : DateOnly.FromDateTime(lastPhoto.Date), today))
                due.Add("Monthly photos are due (front, side, back).");
            BodyDueText = string.Join(" ", due);

            var tests = await Store.GetFitnessTestsAsync(today.AddDays(-400), today);
            var lastTest = tests.LastOrDefault();
            TestText = lastTest == null
                ? "No tests yet."
                : $"{lastTest.Date:MMM d}: " + string.Join(", ", new[]
                {
                    lastTest.ToeTouchInches.HasValue ? $"toe touch {lastTest.ToeTouchInches:+0.#;-0.#;0} in" : null,
                    lastTest.PushUps.HasValue ? $"{lastTest.PushUps} push-ups" : null,
                    lastTest.PlankSeconds.HasValue ? $"plank {lastTest.PlankSeconds} s" : null,
                }.Where(p => p != null));
            TestDueText = TrackerRules.IsMonthlyDue(lastTest == null ? null : DateOnly.FromDateTime(lastTest.Date), today)
                ? "This month's toe-touch test is due."
                : "";
        }
        catch (Exception exception)
        {
            Errors.Record("Loading fitness", exception);
            WeekText = $"Couldn't load: {exception.Message}";
        }
    }

    [RelayCommand]
    Task LogWorkoutAsync() => Shell.Current.GoToAsync("workout");

    [RelayCommand]
    Task OpenWorkoutAsync(WorkoutSession? session) =>
        session == null ? Task.CompletedTask : Shell.Current.GoToAsync($"workout?id={Uri.EscapeDataString(session.Id)}");

    [RelayCommand]
    Task MeasureAsync() => Shell.Current.GoToAsync("measure");
}
