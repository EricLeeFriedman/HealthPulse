using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.ViewModels;

public partial class TodayViewModel : ObservableObject
{
    public ITrackerStore Store;
    public WeatherService Weather;
    public HealthExportService Exporter;
    public ErrorLog Errors;
    public ISettingsStore Settings;
    public IReminderPlatform Reminders;
    bool loading;

    public TodayViewModel(ITrackerStore store, WeatherService weather, HealthExportService exporter, ErrorLog errors,
        ISettingsStore settings, IReminderPlatform reminders)
    {
        Store = store;
        Weather = weather;
        Exporter = exporter;
        Errors = errors;
        Settings = settings;
        Reminders = reminders;
        remindersOn = DailyReminders.IsEnabled(settings);
    }

    bool remindersOn;
    public bool RemindersOn
    {
        get => remindersOn;
        set
        {
            if (!SetProperty(ref remindersOn, value)) return;
            DailyReminders.SetEnabled(Settings, value);
            if (!value) Reminders.ClearShown();
            _ = RefreshRemindersAsync();
        }
    }

    [ObservableProperty] public partial string ReminderText { get; set; } = "";
    [ObservableProperty] public partial string OpenTasksText { get; set; } = "";
    [ObservableProperty] public partial bool CanStopToday { get; set; }
    [ObservableProperty] public partial bool CanResumeToday { get; set; }

    /// <summary>Updates the reminders card, and clears the notification once today's list is done.</summary>
    public async Task RefreshRemindersAsync()
    {
        try
        {
            var now = DateTime.Now;
            var today = DateOnly.FromDateTime(now);
            var tasks = await DailyReminders.LoadOpenTasksAsync(Store, today);
            var stopped = DailyReminders.IsStopped(Settings, today);
            ReminderText = DailyReminders.StatusText(now, Settings, tasks);
            OpenTasksText = tasks.Count == 0 ? "" : DailyReminders.Body(tasks);
            CanStopToday = RemindersOn && !stopped && tasks.Count > 0;
            CanResumeToday = RemindersOn && stopped;
            if (tasks.Count == 0) Reminders.ClearShown();
        }
        catch (Exception exception)
        {
            Errors.Record("Refreshing reminders", exception);
        }
    }

    [RelayCommand]
    Task StopRemindersTodayAsync()
    {
        DailyReminders.StopFor(Settings, DateOnly.FromDateTime(DateTime.Now));
        Reminders.ClearShown();
        return RefreshRemindersAsync();
    }

    [RelayCommand]
    Task ResumeRemindersTodayAsync()
    {
        DailyReminders.Resume(Settings);
        return RefreshRemindersAsync();
    }

    public List<string> StressOptions { get; } =
        ["Not rated", "1 - calm", "2 - a little", "3 - moderate", "4 - high", "5 - very high"];

    [ObservableProperty] public partial DateTime Date { get; set; } = DateTime.Today;
    [ObservableProperty] public partial string PlanText { get; set; } = "";
    [ObservableProperty] public partial string TargetText { get; set; } = "";
    [ObservableProperty] public partial string WeatherText { get; set; } = "";
    [ObservableProperty] public partial string SavedText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaffeineText))]
    public partial double CaffeineCups { get; set; }
    public string CaffeineText => $"Caffeine: {(int)CaffeineCups} cup{((int)CaffeineCups == 1 ? "" : "s")}";
    [ObservableProperty] public partial bool HasLastCaffeine { get; set; }
    [ObservableProperty] public partial TimeSpan LastCaffeineTime { get; set; } = new(12, 0, 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WaterText))]
    public partial double WaterGlasses { get; set; }
    public string WaterText => $"Water: {(int)WaterGlasses} glass{((int)WaterGlasses == 1 ? "" : "es")} (8 oz)";

    [ObservableProperty] public partial bool HasFirstMeal { get; set; }
    [ObservableProperty] public partial TimeSpan FirstMealTime { get; set; } = new(12, 0, 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlcoholText))]
    public partial double AlcoholDrinks { get; set; }
    public string AlcoholText => $"Alcohol: {(int)AlcoholDrinks} drink{((int)AlcoholDrinks == 1 ? "" : "s")}";

    [ObservableProperty] public partial int StressIndex { get; set; }

    [ObservableProperty] public partial bool Riboflavin { get; set; }
    [ObservableProperty] public partial bool BComplex { get; set; }
    [ObservableProperty] public partial bool CoQ10 { get; set; }
    [ObservableProperty] public partial bool Magnesium { get; set; }

    /// <summary>Kept so an existing value survives saves; protein is estimated from meals in the daily review.</summary>
    public string ProteinGrams { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MobilityButtonText))]
    public partial bool ShowMobilityRoutine { get; set; }
    public string MobilityButtonText => ShowMobilityRoutine ? "Hide" : "ⓘ Steps";
    public string MobilityRoutineText { get; } = WorkoutPlan.MobilityRoutineText;

    [RelayCommand]
    void ToggleMobilityRoutine() => ShowMobilityRoutine = !ShowMobilityRoutine;

    [ObservableProperty] public partial ObservableCollection<FoodEntry> Meals { get; set; } = [];
    [ObservableProperty] public partial string NewFoodText { get; set; } = "";
    [ObservableProperty] public partial TimeSpan NewFoodTime { get; set; } = DateTime.Now.TimeOfDay;
    [ObservableProperty] public partial string MealsSummary { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingFood), nameof(FoodButtonText))]
    public partial FoodEntry? EditingFood { get; set; }
    public bool IsEditingFood => EditingFood != null;
    public string FoodButtonText => EditingFood == null ? "Add" : "Save";

    [RelayCommand]
    void CancelFoodEdit()
    {
        EditingFood = null;
        NewFoodText = "";
        NewFoodTime = new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, 0);
    }

    [ObservableProperty] public partial bool MobilityDone { get; set; }
    [ObservableProperty] public partial string MobilityLabel { get; set; } = "Mobility routine done";
    [ObservableProperty] public partial string Notes { get; set; } = "";

    partial void OnDateChanged(DateTime value)
    {
        if (!loading) _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (loading) return;
        loading = true;
        try
        {
            var day = DateOnly.FromDateTime(Date);
            var planned = WorkoutPlan.PlannedFor(day.DayOfWeek);
            PlanText = planned == null
                ? $"{day:dddd}: off. Rest day."
                : $"{day:dddd}: {planned} (block {WorkoutPlan.Block(day)}, week {WorkoutPlan.WeekInBlock(day)})";
            TargetText = planned == null ? "" : WorkoutPlan.TargetFor(planned, day);
            MobilityLabel = day.DayOfWeek == DayOfWeek.Sunday ? "Sunday mobility routine done" : "Mobility routine done";

            var c = await Store.GetCheckInAsync(day);
            CaffeineCups = c.CaffeineCups;
            HasLastCaffeine = c.LastCaffeineTime.HasValue;
            LastCaffeineTime = c.LastCaffeineTime ?? new TimeSpan(12, 0, 0);
            WaterGlasses = c.WaterGlasses;
            HasFirstMeal = c.FirstMealTime.HasValue;
            FirstMealTime = c.FirstMealTime ?? new TimeSpan(12, 0, 0);
            AlcoholDrinks = c.AlcoholDrinks;
            StressIndex = c.Stress ?? 0;
            Riboflavin = c.Riboflavin;
            BComplex = c.BComplex;
            CoQ10 = c.CoQ10;
            Magnesium = c.Magnesium;
            ProteinGrams = c.ProteinGrams?.ToString(CultureInfo.CurrentCulture) ?? "";
            MobilityDone = c.MobilityDone;
            Notes = c.Notes ?? "";
            SavedText = c.UpdatedAt == DateTime.MinValue
                ? "Not checked in yet"
                : $"Saved {DateTime.SpecifyKind(c.UpdatedAt, DateTimeKind.Utc).ToLocalTime():h:mm tt}";

            await LoadMealsAsync(day);
            NewFoodTime = new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, 0);

            await RefreshWeatherTextAsync(day);
            await RefreshRemindersAsync();
        }
        catch (Exception exception)
        {
            Errors.Record("Loading check-in", exception);
            SavedText = $"Couldn't load: {exception.Message}";
        }
        finally
        {
            loading = false;
        }
    }

    async Task RefreshWeatherTextAsync(DateOnly day)
    {
        var stored = (await Store.GetWeatherAsync(day, day)).FirstOrDefault();
        WeatherText = Describe(stored);
        if (day == DateOnly.FromDateTime(DateTime.Today))
        {
            var error = await Weather.RefreshAsync(day, 14, interactive: true);
            stored = (await Store.GetWeatherAsync(day, day)).FirstOrDefault();
            WeatherText = stored != null ? Describe(stored) : error ?? Describe(null);
        }
    }

    public static string Describe(WeatherDay? day)
    {
        if (day?.PressureMeanHpa == null) return "No pressure data yet.";
        var text = $"Pressure {day.PressureMeanHpa:0} hPa";
        if (day.PressureChangeHpa is { } change)
            text += change switch
            {
                <= -0.5 => $", down {-change:0.#} since yesterday",
                >= 0.5 => $", up {change:0.#} since yesterday",
                _ => ", steady"
            };
        if (day.PressureMaxDrop6hHpa is >= 3) text += $". Fast drop: {day.PressureMaxDrop6hHpa:0.#} hPa in 6 h";
        if (day.TempMaxF.HasValue) text += $". {day.TempMinF:0}–{day.TempMaxF:0}°F";
        return text + ".";
    }

    [RelayCommand]
    void AllSupplements()
    {
        Riboflavin = BComplex = CoQ10 = Magnesium = true;
    }

    [RelayCommand]
    void CaffeineNow()
    {
        CaffeineCups += 1;
        HasLastCaffeine = true;
        LastCaffeineTime = new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, 0);
    }

    [RelayCommand]
    void MealNow()
    {
        HasFirstMeal = true;
        FirstMealTime = new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, 0);
    }

    async Task LoadMealsAsync(DateOnly day)
    {
        var meals = await Store.GetFoodAsync(day, day);
        Meals = new ObservableCollection<FoodEntry>(meals);
        MealsSummary = meals.Count == 0
            ? "Nothing logged yet. Write what you ate with rough portions, e.g. \"3 eggs, 1 slice toast, latte\"."
            : $"{meals.Count} entr{(meals.Count == 1 ? "y" : "ies")}"  + ". Tap one to edit or delete it.";
    }

    DailyCheckIn BuildCheckIn() => new()
    {
        Date = DailyCheckIn.Key(DateOnly.FromDateTime(Date)),
        CaffeineCups = (int)CaffeineCups,
        LastCaffeineTime = HasLastCaffeine ? LastCaffeineTime : null,
        WaterGlasses = (int)WaterGlasses,
        FirstMealTime = HasFirstMeal ? FirstMealTime : null,
        AlcoholDrinks = (int)AlcoholDrinks,
        Stress = StressIndex > 0 ? StressIndex : null,
        Riboflavin = Riboflavin,
        BComplex = BComplex,
        CoQ10 = CoQ10,
        Magnesium = Magnesium,
        ProteinGrams = TrackerRules.ParseInt(ProteinGrams, "Protein"),
        MobilityDone = MobilityDone,
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
    };

    async Task SaveCheckInAsync(DailyCheckIn checkIn)
    {
        await Store.SaveCheckInAsync(checkIn);
        SavedText = $"Saved {DateTime.Now:h:mm tt}";
        _ = Exporter.ExportIfDueAsync();
        await RefreshRemindersAsync();
    }

    /// <summary>Meals fill in the day's protein total and first meal time, then the check-in is saved.</summary>
    async Task ApplyMealsAsync()
    {
        var checkIn = BuildCheckIn();
        FoodLog.ApplyTo(checkIn, Meals);
        ProteinGrams = checkIn.ProteinGrams?.ToString(CultureInfo.CurrentCulture) ?? "";
        HasFirstMeal = checkIn.FirstMealTime.HasValue;
        if (checkIn.FirstMealTime is { } first) FirstMealTime = first;
        await SaveCheckInAsync(checkIn);
    }

    [RelayCommand]
    Task AddFoodAsync() => UiActions.RunAsync(Errors, "Saving food", async () =>
    {
        var entry = EditingFood ?? new FoodEntry();
        entry.Time = Date.Date + NewFoodTime;
        entry.Text = NewFoodText;
        await Store.SaveFoodAsync(entry);
        EditingFood = null;
        NewFoodText = "";
        await LoadMealsAsync(DateOnly.FromDateTime(Date));
        await ApplyMealsAsync();
    });

    /// <summary>Tap an entry to edit or delete it.</summary>
    [RelayCommand]
    Task FoodTappedAsync(FoodEntry? entry) => UiActions.RunAsync(Errors, "Changing food", async () =>
    {
        if (entry == null) return;
        var choice = await Shell.Current.DisplayActionSheetAsync(entry.Text, "Cancel", "Delete", "Edit");
        if (choice == "Edit")
        {
            EditingFood = entry;
            NewFoodText = entry.Text;
            NewFoodTime = entry.Time.TimeOfDay;
        }
        else if (choice == "Delete")
        {
            await Store.DeleteFoodAsync(entry.Id);
            if (EditingFood?.Id == entry.Id) CancelFoodEdit();
            await LoadMealsAsync(DateOnly.FromDateTime(Date));
            await ApplyMealsAsync();
        }
    });

    [RelayCommand]
    Task SaveAsync() => UiActions.RunAsync(Errors, "Saving check-in", () => SaveCheckInAsync(BuildCheckIn()));
}
