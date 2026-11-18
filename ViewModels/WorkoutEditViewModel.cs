using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.ViewModels;

public partial class SetLog : ObservableObject
{
    public SetLog(int number, bool weighted, bool timed, string amount = "", string weight = "")
    {
        Number = number;
        Weighted = weighted;
        Timed = timed;
        Amount = amount;
        Weight = weight;
    }

    public int Number { get; }
    public bool Weighted { get; }
    public bool Timed { get; }
    public string Label => $"Set {Number}";
    public string AmountPlaceholder => Timed ? "seconds" : "reps";

    [ObservableProperty] public partial string Amount { get; set; }
    [ObservableProperty] public partial string Weight { get; set; }
}

public partial class ExerciseLog : ObservableObject
{
    public ExerciseLog(ExerciseTemplate template, string hint)
    {
        Template = template;
        Hint = hint;
    }

    public ExerciseTemplate Template { get; }
    public string Name => Template.Name;
    public string Unit => Template.Unit + (Template.Weighted ? ", lb" : "");
    public string Hint { get; }
    public bool HasHint => !string.IsNullOrEmpty(Hint);
    public string HowTo => ExerciseGuide.For(Template.Name) is { } guide ? $"{guide.How} {guide.Load}" : "";
    public bool HasHowTo => HowTo.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HowToButtonText))]
    public partial bool ShowHowTo { get; set; }
    public string HowToButtonText => ShowHowTo ? "Hide" : "ⓘ How";

    [RelayCommand]
    void ToggleHowTo() => ShowHowTo = !ShowHowTo;
    public ObservableCollection<SetLog> Sets { get; } = [];

    [RelayCommand]
    void AddSet()
    {
        var last = Sets.LastOrDefault();
        Sets.Add(new SetLog(Sets.Count + 1, Template.Weighted, Template.Timed, last?.Amount ?? "", last?.Weight ?? ""));
    }

    [RelayCommand]
    void RemoveSet()
    {
        if (Sets.Count > 0) Sets.RemoveAt(Sets.Count - 1);
    }
}

public partial class WorkoutEditViewModel : ObservableObject, IQueryAttributable
{
    public ITrackerStore Store;
    public HealthExportService Exporter;
    public ErrorLog Errors;
    WorkoutSession session = new();
    List<WorkoutSession> history = [];
    bool building;

    public WorkoutEditViewModel(ITrackerStore store, HealthExportService exporter, ErrorLog errors)
    {
        Store = store;
        Exporter = exporter;
        Errors = errors;
    }

    public List<string> SessionTypes { get; } = [.. WorkoutPlan.SessionTypes];

    [ObservableProperty] public partial string PageTitle { get; set; } = "Log workout";
    [ObservableProperty] public partial bool IsExisting { get; set; }
    [ObservableProperty] public partial DateTime Date { get; set; } = DateTime.Today;
    [ObservableProperty] public partial TimeSpan Clock { get; set; } = DateTime.Now.TimeOfDay;

    [RelayCommand]
    void SessionNow() => (Date, Clock) = ClockNow.Stamp(DateTime.Now);
    [ObservableProperty] public partial int TypeIndex { get; set; } = -1;
    [ObservableProperty] public partial string TargetText { get; set; } = "";
    [ObservableProperty] public partial string Duration { get; set; } = "";
    [ObservableProperty] public partial string Distance { get; set; } = "";
    [ObservableProperty] public partial string Notes { get; set; } = "";
    [ObservableProperty] public partial bool IsRun { get; set; }
    [ObservableProperty] public partial bool IsMobility { get; set; }
    [ObservableProperty] public partial string NewExercise { get; set; } = "";
    [ObservableProperty] public partial string DetectedText { get; set; } = "";
    [ObservableProperty] public partial bool IsDetected { get; set; }
    [ObservableProperty] public partial ObservableCollection<ExerciseLog> Exercises { get; set; } = [];

    public string MobilityText => string.Join("\n", WorkoutPlan.MobilityRoutine.Select((m, i) => $"{i + 1}. {m}"));

    string SelectedType => TypeIndex >= 0 && TypeIndex < SessionTypes.Count ? SessionTypes[TypeIndex] : "";

    bool started;

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        _ = LoadAsync(query.TryGetValue("id", out var value) && value is string id ? Uri.UnescapeDataString(id) : null);

    /// <summary>Loads once: from the "id" query when editing, or a new workout for today's plan.</summary>
    public async Task LoadAsync(string? id)
    {
        if (started) return;
        started = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            history = await Store.GetWorkoutsAsync(today.AddDays(-120), today);
            if (id != null && await Store.GetWorkoutAsync(id) is { } existing)
            {
                session = existing;
                IsExisting = true;
                PageTitle = "Edit workout";
                building = true;
                Date = existing.Date.Date;
                Clock = existing.Date.TimeOfDay;
                Duration = existing.DurationMinutes?.ToString(CultureInfo.CurrentCulture) ?? "";
                Distance = existing.DistanceMiles?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
                Notes = existing.Notes ?? "";
                TypeIndex = SessionTypes.IndexOf(existing.SessionType);
                IsDetected = existing.ExternalId != null;
                if (IsDetected)
                {
                    var details = new List<string> { (existing.ExerciseType ?? "workout").Replace('_', ' ') };
                    if (existing.DurationMinutes.HasValue) details.Add($"{existing.DurationMinutes} min");
                    if (existing.DistanceMiles.HasValue) details.Add($"{existing.DistanceMiles:0.##} mi");
                    if (existing.AvgHeartRate.HasValue) details.Add($"avg heart rate {existing.AvgHeartRate:0}");
                    if (existing.MaxHeartRate.HasValue) details.Add($"max {existing.MaxHeartRate:0}");
                    DetectedText = $"⌚ From your watch: {string.Join(", ", details)}. Change the session if it was a different one. Sets and weights are optional.";
                }
                building = false;
                BuildExercises(existing.Sets);
            }
            else
            {
                session = new WorkoutSession();
                TypeIndex = SessionTypes.IndexOf(WorkoutPlan.PlannedFor(today.DayOfWeek) ?? WorkoutPlan.Other);
            }
        }
        catch (Exception exception)
        {
            Errors.Record("Opening workout", exception);
        }
    }

    partial void OnTypeIndexChanged(int value)
    {
        if (!building) BuildExercises([]);
    }

    partial void OnDateChanged(DateTime value)
    {
        if (!building && !IsExisting) BuildExercises([]);
    }

    void BuildExercises(IReadOnlyList<WorkoutSet> saved)
    {
        var type = SelectedType;
        var day = DateOnly.FromDateTime(Date);
        IsRun = type == WorkoutPlan.Run;
        IsMobility = type == WorkoutPlan.Mobility;
        TargetText = WorkoutPlan.TargetFor(type, day);

        var templates = WorkoutPlan.Exercises(type, day).ToList();
        // Keep any saved exercise that isn't in the template (custom or older plan week).
        foreach (var name in saved.Select(s => s.Exercise).Distinct())
        {
            if (templates.All(t => t.Name != name))
            {
                var sample = saved.First(s => s.Exercise == name);
                templates.Add(new ExerciseTemplate(name, sample.WeightLbs.HasValue, sample.Seconds.HasValue));
            }
        }

        var targetSets = WorkoutPlan.TargetSets(day);
        var list = new ObservableCollection<ExerciseLog>();
        foreach (var template in templates)
        {
            var last = WorkoutPlan.LastSets(template.Name, history.Where(h => h.Id != session.Id));
            var hint = WorkoutPlan.LastTimeText(last);
            if (WorkoutPlan.ShouldGoUp(last, day)) hint += ". You hit 12 on every set: go up one dumbbell size.";
            var log = new ExerciseLog(template, hint);
            var mine = saved.Where(s => s.Exercise == template.Name).OrderBy(s => s.Order).ToList();
            if (mine.Count > 0)
            {
                foreach (var set in mine)
                    log.Sets.Add(new SetLog(log.Sets.Count + 1, template.Weighted, template.Timed,
                        (template.Timed ? set.Seconds : set.Reps)?.ToString(CultureInfo.CurrentCulture) ?? "",
                        set.WeightLbs?.ToString("0.#", CultureInfo.CurrentCulture) ?? ""));
            }
            else if (saved.Count == 0)
            {
                // Prefill the weight from last time; reps are typed after each set.
                var weight = last.FirstOrDefault()?.WeightLbs?.ToString("0.#", CultureInfo.CurrentCulture) ?? "";
                for (var i = 0; i < targetSets; i++)
                    log.Sets.Add(new SetLog(i + 1, template.Weighted, template.Timed, "", weight));
            }
            list.Add(log);
        }
        Exercises = list;
    }

    [RelayCommand]
    void AddExercise()
    {
        if (string.IsNullOrWhiteSpace(NewExercise)) return;
        var log = new ExerciseLog(new ExerciseTemplate(NewExercise.Trim(), Weighted: true), "");
        log.Sets.Add(new SetLog(1, true, false));
        Exercises.Add(log);
        NewExercise = "";
    }

    [RelayCommand]
    Task SaveAsync() => UiActions.RunAsync(Errors, "Saving workout", async () =>
    {
        if (SelectedType == "") throw new ArgumentException("Pick a session type.");
        session.SessionType = SelectedType;
        session.Date = Date.Date + Clock;
        session.DurationMinutes = TrackerRules.ParseInt(Duration, "Duration");
        session.DistanceMiles = TrackerRules.ParseDouble(Distance, "Distance");
        session.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        session.Sets = WorkoutForm.BuildSets(Exercises.SelectMany(e =>
            e.Sets.Select(s => new SetInput(e.Name, e.Template.Timed, s.Amount, s.Weight))));
        await Store.SaveWorkoutAsync(session);
        _ = Exporter.ExportIfDueAsync();
        await Shell.Current.GoToAsync("..");
    });

    [RelayCommand]
    Task DeleteAsync() => UiActions.RunAsync(Errors, "Deleting workout", async () =>
    {
        if (!IsExisting) return;
        var message = IsDetected
            ? "It won't be imported from your watch again."
            : "This can't be undone.";
        if (!await Shell.Current.DisplayAlertAsync("Delete workout?", message, "Delete", "Cancel")) return;
        await Store.DeleteWorkoutAsync(session.Id);
        await Shell.Current.GoToAsync("..");
    });
}
