using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.ViewModels;

public partial class SetLog : ObservableObject
{
    public SetLog(int number, bool weighted, bool timed, string amount = "", string weight = "",
        int? repsInReserve = null, string placeholder = "")
    {
        Number = number;
        Weighted = weighted;
        Timed = timed;
        Amount = amount;
        Weight = weight;
        EffortIndex = repsInReserve is >= 0 ? Math.Min(repsInReserve.Value, 3) : -1;
        AmountPlaceholder = string.IsNullOrEmpty(placeholder) ? (timed ? "seconds" : "reps") : placeholder;
    }

    public int Number { get; }
    public bool Weighted { get; }
    public bool Timed { get; }
    public string Label => $"Set {Number}";
    public string AmountPlaceholder { get; }
    public List<string> EffortOptions { get; } = [.. StrengthProgress.EffortOptions];

    /// <summary>Reps in reserve picked for the set (0 to 3+), or -1 when not picked.</summary>
    [ObservableProperty] public partial int EffortIndex { get; set; }

    public int? RepsInReserve => EffortIndex >= 0 ? EffortIndex : null;

    [ObservableProperty] public partial string Amount { get; set; }
    [ObservableProperty] public partial string Weight { get; set; }
}

public partial class ExerciseLog : ObservableObject
{
    public ExerciseLog(ExerciseTemplate template, string hint, Recommendation? advice = null)
    {
        Template = template;
        Hint = hint;
        Advice = advice;
    }

    /// <summary>What to do this time, from the last sessions (double progression).</summary>
    public Recommendation? Advice { get; }
    public bool HasAdvice => Advice != null;
    public string AdviceTag => Advice?.Tag ?? "";
    public string AdviceText => Advice?.Reason ?? "";
    public Color AdviceColor => Advice switch
    {
        { GoUp: true } => Color.FromArgb("#15803D"),
        { BackOff: true } => Color.FromArgb("#B45309"),
        _ => Color.FromArgb("#374151"),
    };

    /// <summary>"10-12" style placeholder for the reps box.</summary>
    public string TargetPlaceholder => Advice == null ? "" :
        (Advice.Target.Min == Advice.Target.Max ? $"{Advice.Target.Max}" : $"{Advice.Target.Min}-{Advice.Target.Max}")
        + (Template.Timed ? " s" : " reps");

    public ExerciseTemplate Template { get; }
    public string Name => Template.Name;
    public string Unit => Template.Unit + (Template.Weighted ? ", lb per dumbbell" : "");
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
        Sets.Add(new SetLog(Sets.Count + 1, Template.Weighted, Template.Timed, "", last?.Weight ?? "", placeholder: TargetPlaceholder));
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
    [ObservableProperty] public partial bool IsStrength { get; set; }

    public string EffortHelp =>
        "After each set, type the reps and pick how many more good reps you had left (0 = couldn't do another). " +
        "Weights are pre-filled with today's recommendation. When every set reaches the top of the range with 2 or more left, the next session goes up.";
    [ObservableProperty] public partial string NewExercise { get; set; } = "";
    [ObservableProperty] public partial string DetectedText { get; set; } = "";
    [ObservableProperty] public partial bool IsDetected { get; set; }
    [ObservableProperty] public partial ObservableCollection<ExerciseLog> Exercises { get; set; } = [];

    public List<MobilityMoveItem> MobilityMoves { get; } = MobilityMoveItem.All();

    string SelectedType => TypeIndex >= 0 && TypeIndex < SessionTypes.Count ? SessionTypes[TypeIndex] : "";

    bool started;

    public const string AsPlanned = "As planned";

    [ObservableProperty] public partial List<string> AlternativeOptions { get; set; } = [AsPlanned];
    [ObservableProperty] public partial int AlternativeIndex { get; set; }
    [ObservableProperty] public partial bool HasAlternatives { get; set; }
    [ObservableProperty] public partial string AlternativeHow { get; set; } = "";
    [ObservableProperty] public partial bool HasAlternativeHow { get; set; }

    string? SelectedAlternative =>
        AlternativeIndex > 0 && AlternativeIndex < AlternativeOptions.Count ? AlternativeOptions[AlternativeIndex] : null;

    partial void OnAlternativeIndexChanged(int value) => ShowAlternativeHow();

    void ShowAlternativeHow()
    {
        var name = SelectedAlternative;
        AlternativeHow = name == null
            ? ""
            : WorkoutAlternatives.For(SelectedType, DateOnly.FromDateTime(Date)).FirstOrDefault(a => a.Name == name)?.How ?? "";
        HasAlternativeHow = AlternativeHow.Length > 0;
    }

    /// <summary>
    /// Fills the "Done as" picker for the session type, keeping <paramref name="keep"/> selected.
    /// A saved name that isn't a listed option (e.g. "Elliptical" from the watch) is added when
    /// <paramref name="keepUnknown"/> is set.
    /// </summary>
    void BuildAlternatives(string? keep, bool keepUnknown = false)
    {
        var options = new List<string> { AsPlanned };
        options.AddRange(WorkoutAlternatives.NamesFor(SelectedType, DateOnly.FromDateTime(Date)));
        if (!string.IsNullOrEmpty(keep) && !options.Contains(keep))
        {
            if (keepUnknown) options.Add(keep);
            else keep = null;
        }
        AlternativeIndex = -1;
        AlternativeOptions = options;
        AlternativeIndex = string.IsNullOrEmpty(keep) ? 0 : options.IndexOf(keep);
        HasAlternatives = options.Count > 1;
        ShowAlternativeHow();
    }

    static string? Query(IDictionary<string, object> query, string key) =>
        query.TryGetValue(key, out var value) && value is string text ? Uri.UnescapeDataString(text) : null;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        DateOnly? date = DateOnly.TryParseExact(Query(query, "date") ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : null;
        _ = LoadAsync(Query(query, "id"), Query(query, "type"), Query(query, "alt"), date);
    }

    /// <summary>
    /// Loads once: from the "id" query when editing, or a new workout for the day's plan
    /// (or the given session type and alternative, from the Today tab).
    /// </summary>
    public async Task LoadAsync(string? id, string? type = null, string? alternative = null, DateOnly? date = null)
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
                BuildAlternatives(existing.Alternative, keepUnknown: true);
            }
            else
            {
                session = new WorkoutSession();
                var day = date ?? today;
                building = true;
                if (day != today)
                {
                    Date = day.ToDateTime(TimeOnly.MinValue);
                    Clock = new TimeSpan(12, 0, 0);
                }
                building = false;
                var swaps = await Store.GetPlanSwapsAsync(day, day);
                var planned = type != null && SessionTypes.Contains(type) ? type : WorkoutPlan.PlannedFor(day, swaps);
                TypeIndex = SessionTypes.IndexOf(planned ?? WorkoutPlan.Other);
                BuildAlternatives(alternative, keepUnknown: true);
            }
        }
        catch (Exception exception)
        {
            Errors.Record("Opening workout", exception);
        }
    }

    partial void OnTypeIndexChanged(int value)
    {
        if (building) return;
        BuildExercises([]);
        BuildAlternatives(SelectedAlternative);
    }

    partial void OnDateChanged(DateTime value)
    {
        if (building || IsExisting) return;
        BuildExercises([]);
        BuildAlternatives(SelectedAlternative);
    }

    void BuildExercises(IReadOnlyList<WorkoutSet> saved)
    {
        var type = SelectedType;
        var day = DateOnly.FromDateTime(Date);
        IsRun = type == WorkoutPlan.Run;
        IsMobility = type == WorkoutPlan.Mobility;
        IsStrength = WorkoutPlan.IsStrength(type);
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
            // Only sessions before this one count, so editing an old workout shows the advice it had.
            var earlier = history.Where(h => h.Id != session.Id && (!IsExisting || h.Date < session.Date)).ToList();
            var last = WorkoutPlan.LastSets(template.Name, earlier);
            var hint = WorkoutPlan.LastTimeText(last);
            var advice = StrengthProgress.Recommend(template, StrengthProgress.History(template.Name, earlier), day);
            var log = new ExerciseLog(template, hint, advice);
            var mine = saved.Where(s => s.Exercise == template.Name).OrderBy(s => s.Order).ToList();
            if (mine.Count > 0)
            {
                foreach (var set in mine)
                    log.Sets.Add(new SetLog(log.Sets.Count + 1, template.Weighted, template.Timed,
                        (template.Timed ? set.Seconds : set.Reps)?.ToString(CultureInfo.CurrentCulture) ?? "",
                        set.WeightLbs?.ToString("0.#", CultureInfo.CurrentCulture) ?? "",
                        set.RepsInReserve, log.TargetPlaceholder));
            }
            else if (saved.Count == 0)
            {
                // Prefill the recommended weight (last time's, or one step up or down);
                // reps and reps left are typed after each set.
                var lastWeight = last.Max(s => s.WeightLbs);
                var weight = (advice.WeightLbs ?? lastWeight)?.ToString("0.#", CultureInfo.CurrentCulture) ?? "";
                for (var i = 0; i < targetSets; i++)
                    log.Sets.Add(new SetLog(i + 1, template.Weighted, template.Timed, "", weight, placeholder: log.TargetPlaceholder));
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
        session.Alternative = SelectedAlternative;
        session.Sets = WorkoutForm.BuildSets(Exercises.SelectMany(e =>
            e.Sets.Select(s => new SetInput(e.Name, e.Template.Timed, s.Amount, s.Weight, s.RepsInReserve))));
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
