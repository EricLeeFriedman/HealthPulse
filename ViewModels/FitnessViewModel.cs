using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.ViewModels;

public partial class FitnessViewModel : ObservableObject
{
    public ITrackerStore Store;
    public ErrorLog Errors;

    public FitnessViewModel(ITrackerStore store, ErrorLog errors)
    {
        Store = store;
        Errors = errors;
    }

    [ObservableProperty] public partial string WeekText { get; set; } = "";
    [ObservableProperty] public partial string TodayText { get; set; } = "";
    [ObservableProperty] public partial string TargetText { get; set; } = "";
    [ObservableProperty] public partial string BodyText { get; set; } = "";
    [ObservableProperty] public partial string BodyDueText { get; set; } = "";
    [ObservableProperty] public partial string TestText { get; set; } = "";
    [ObservableProperty] public partial string TestDueText { get; set; } = "";
    [ObservableProperty] public partial ObservableCollection<WorkoutSession> Workouts { get; set; } = [];

    public async Task LoadAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var weekStart = WorkoutPlan.WeekStart(today);
            var workouts = await Store.GetWorkoutsAsync(today.AddDays(-60), today);
            var checkIns = await Store.GetCheckInsAsync(weekStart, today);
            WeekText = WorkoutPlan.Progress(weekStart, workouts, checkIns).Text;

            var planned = WorkoutPlan.PlannedFor(today.DayOfWeek);
            var doneToday = workouts.Any(w => DateOnly.FromDateTime(w.Date) == today);
            TodayText = planned == null
                ? "Today: off (Saturday)."
                : $"Today: {planned}{(doneToday ? " ✓ logged" : "")} • block {WorkoutPlan.Block(today)}, week {WorkoutPlan.WeekInBlock(today)}";
            TargetText = planned == null ? "" : WorkoutPlan.TargetFor(planned, today);
            Workouts = new ObservableCollection<WorkoutSession>(workouts.OrderByDescending(w => w.Date));

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
