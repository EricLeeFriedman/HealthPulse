using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.ViewModels;

/// <summary>One alternative on the Today tab, with its own How toggle.</summary>
public partial class AlternativeItem : ObservableObject
{
    public AlternativeItem(string sessionType, WorkoutAlternative alternative)
    {
        SessionType = sessionType;
        Name = alternative.Name;
        How = alternative.How;
    }

    public string SessionType { get; }
    public string Name { get; }
    public string How { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HowButtonText))]
    public partial bool ShowHow { get; set; }
    public string HowButtonText => ShowHow ? "Hide" : "ⓘ How";

    [RelayCommand]
    void ToggleHow() => ShowHow = !ShowHow;
}

/// <summary>An ingredient chip in the food log; tap to tick or untick it.</summary>
public partial class IngredientChip : ObservableObject
{
    public IngredientChip(string name, bool isSelected = false)
    {
        Name = name;
        IsSelected = isSelected;
    }

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    public partial bool IsSelected { get; set; }
    public string Text => IsSelected ? $"✓ {Name}" : Name;
}

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
    [ObservableProperty] public partial string WorkoutGuideText { get; set; } = "";
    [ObservableProperty] public partial bool HasWorkoutGuide { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkoutGuideButtonText))]
    public partial bool ShowWorkoutGuide { get; set; }
    public string WorkoutGuideButtonText => ShowWorkoutGuide ? "Hide" : "ⓘ How to do today's workout";

    [RelayCommand]
    void ToggleWorkoutGuide() => ShowWorkoutGuide = !ShowWorkoutGuide;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlternativesButtonText))]
    public partial bool ShowAlternatives { get; set; }
    public string AlternativesButtonText => ShowAlternatives ? "Hide" : "⇄ Alternatives or swap a day";

    [RelayCommand]
    void ToggleAlternatives() => ShowAlternatives = !ShowAlternatives;

    [ObservableProperty] public partial bool HasAlternativesSection { get; set; }
    [ObservableProperty] public partial ObservableCollection<AlternativeItem> Alternatives { get; set; } = [];
    [ObservableProperty] public partial bool HasAlternatives { get; set; }
    [ObservableProperty] public partial string AlternativesIntro { get; set; } = "";

    List<PlanSwap> swaps = [];
    List<DateOnly> swapDays = [];
    [ObservableProperty] public partial List<string> SwapOptions { get; set; } = [];
    [ObservableProperty] public partial int SwapIndex { get; set; } = -1;
    [ObservableProperty] public partial bool CanSwap { get; set; }
    [ObservableProperty] public partial bool IsSwapped { get; set; }
    [ObservableProperty] public partial string SwapNoteText { get; set; } = "";

    /// <summary>The day's plan after swaps, its guide, and its alternatives.</summary>
    async Task LoadPlanAsync(DateOnly day)
    {
        var week = WorkoutPlan.WeekStart(day);
        swaps = await Store.GetPlanSwapsAsync(week, week.AddDays(6));
        var planned = WorkoutPlan.PlannedFor(day, swaps);
        IsSwapped = WorkoutPlan.IsSwapped(day, swaps);
        SwapNoteText = IsSwapped ? WorkoutPlan.SwapNote(day, swaps) : "";
        PlanText = planned == null
            ? $"{day:dddd}: off. Rest day."
            : $"{day:dddd}: {planned} (block {WorkoutPlan.Block(day)}, week {WorkoutPlan.WeekInBlock(day)})";
        if (IsSwapped) PlanText += $"\n{SwapNoteText}";
        TargetText = planned == null ? "" : WorkoutPlan.TargetFor(planned, day);
        WorkoutGuideText = planned == null || planned == WorkoutPlan.Mobility ? "" : ExerciseGuide.SessionText(planned, day);
        HasWorkoutGuide = WorkoutGuideText.Length > 0;
        if (!HasWorkoutGuide) ShowWorkoutGuide = false;

        var alternatives = WorkoutAlternatives.For(planned, day);
        Alternatives = new ObservableCollection<AlternativeItem>(alternatives.Select(a => new AlternativeItem(planned!, a)));
        HasAlternatives = Alternatives.Count > 0;
        AlternativesIntro = planned == WorkoutPlan.Run
            ? "Raining or too cold? Any of these at the same effort counts as today's run. Tap Log to record it."
            : "Can't do it as planned? Either of these counts as today's session. Tap Log to record it.";

        swapDays = WorkoutPlan.SwapOptions(day, swaps);
        SwapIndex = -1;
        SwapOptions = swapDays.Select(d => $"{d:dddd}: {WorkoutPlan.SessionName(WorkoutPlan.PlannedFor(d, swaps))}").ToList();
        CanSwap = !IsSwapped && swapDays.Count > 0;
        HasAlternativesSection = HasAlternatives || CanSwap || IsSwapped;
        if (!HasAlternativesSection) ShowAlternatives = false;
    }

    /// <summary>Opens the workout form with the alternative picked, for the day shown.</summary>
    [RelayCommand]
    Task LogAlternativeAsync(AlternativeItem? item) => UiActions.RunAsync(Errors, "Opening workout", () =>
    {
        if (item == null) return Task.CompletedTask;
        var day = DailyCheckIn.Key(DateOnly.FromDateTime(Date));
        return Shell.Current.GoToAsync(
            $"workout?type={Uri.EscapeDataString(item.SessionType)}&alt={Uri.EscapeDataString(item.Name)}&date={day}");
    });

    [RelayCommand]
    Task SwapAsync() => UiActions.RunAsync(Errors, "Swapping days", async () =>
    {
        if (SwapIndex < 0 || SwapIndex >= swapDays.Count) throw new ArgumentException("Pick a day to swap with.");
        var day = DateOnly.FromDateTime(Date);
        await Store.SavePlanSwapsAsync(WorkoutPlan.Swap(day, swapDays[SwapIndex], swaps));
        await LoadPlanAsync(day);
        await RefreshRemindersAsync();
    });

    [RelayCommand]
    Task UndoSwapAsync() => UiActions.RunAsync(Errors, "Undoing swap", async () =>
    {
        var day = DateOnly.FromDateTime(Date);
        await Store.DeletePlanSwapsAsync(WorkoutPlan.UndoSwap(day, swaps));
        await LoadPlanAsync(day);
        await RefreshRemindersAsync();
    });
    [ObservableProperty] public partial string WeatherText { get; set; } = "";
    [ObservableProperty] public partial string SavedText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaffeineText))]
    public partial double CaffeineCups { get; set; }
    public string CaffeineText => $"Caffeine: {(int)CaffeineCups} cup{((int)CaffeineCups == 1 ? "" : "s")}";
    [ObservableProperty] public partial bool HasLastCaffeine { get; set; }
    [ObservableProperty] public partial TimeSpan LastCaffeineTime { get; set; } = new(12, 0, 0);

    /// <summary>The day's water in fluid ounces.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WaterText), nameof(WaterStatus), nameof(WaterProgress))]
    public partial int WaterOunces { get; set; }
    public string WaterText => WaterLog.Text(WaterOunces);
    public string WaterStatus => WaterLog.Status(WaterOunces);
    public double WaterProgress => Math.Min(1.0, WaterOunces / (double)WaterLog.TargetOunces);
    public IReadOnlyList<WaterAmount> WaterAmounts { get; } = WaterLog.QuickAmounts;
    [ObservableProperty] public partial string CustomWaterOunces { get; set; } = "";

    /// <summary>Amounts added since the day was loaded, so the last one can be undone.</summary>
    readonly Stack<int> waterAdds = new();
    [ObservableProperty] public partial bool CanUndoWater { get; set; }

    /// <summary>Adds water and saves the check-in, so a tap is never lost.</summary>
    Task ChangeWaterAsync(int ounces) => UiActions.RunAsync(Errors, "Saving water", async () =>
    {
        var before = WaterOunces;
        WaterOunces = WaterLog.Add(WaterOunces, ounces);
        var applied = WaterOunces - before;
        if (applied == 0) return;
        waterAdds.Push(applied);
        CanUndoWater = true;
        await SaveCheckInAsync(BuildCheckIn());
    });

    [RelayCommand]
    Task AddWaterAmountAsync(WaterAmount? amount) => amount == null ? Task.CompletedTask : ChangeWaterAsync(amount.Ounces);

    [RelayCommand]
    Task AddCustomWaterAsync() => UiActions.RunAsync(Errors, "Adding water", async () =>
    {
        var ounces = WaterLog.ParseOunces(CustomWaterOunces);
        CustomWaterOunces = "";
        await ChangeWaterAsync(ounces);
    });

    [RelayCommand]
    Task RemoveCustomWaterAsync() => UiActions.RunAsync(Errors, "Removing water", async () =>
    {
        var ounces = WaterLog.ParseOunces(CustomWaterOunces);
        CustomWaterOunces = "";
        await ChangeWaterAsync(-ounces);
    });

    [RelayCommand]
    Task UndoWaterAsync() => UiActions.RunAsync(Errors, "Undoing water", async () =>
    {
        if (waterAdds.Count == 0) return;
        WaterOunces = WaterLog.Add(WaterOunces, -waterAdds.Pop());
        CanUndoWater = waterAdds.Count > 0;
        await SaveCheckInAsync(BuildCheckIn());
    });

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
        DiscardUnsavedPhoto();
        EditingFood = null;
        NewFoodText = "";
        NewFoodTime = ClockNow.Time(DateTime.Now);
        pickedFood = null;
        ResetFoodDetails();
    }

    /// <summary>Portion picker options, each with the size it stands for.</summary>
    public List<string> PortionChoices { get; } = [.. FoodLog.PortionChoices];

    [ObservableProperty] public partial string NewFoodPortion { get; set; } = FoodLog.NoPortion;

    /// <summary>Common ingredients plus ones typed in recently; ticked ones are saved with the entry.</summary>
    [ObservableProperty] public partial ObservableCollection<IngredientChip> IngredientChips { get; set; } = [];
    [ObservableProperty] public partial string CustomIngredient { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewFoodPhoto), nameof(NewFoodPhoto))]
    public partial string? NewFoodPhotoPath { get; set; }
    public bool HasNewFoodPhoto => !string.IsNullOrEmpty(NewFoodPhotoPath);
    public ImageSource? NewFoodPhoto => HasNewFoodPhoto ? ImageSource.FromFile(NewFoodPhotoPath) : null;

    List<string> SelectedIngredients => IngredientChips.Where(c => c.IsSelected).Select(c => c.Name).ToList();

    void SetIngredients(IEnumerable<string> names)
    {
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var chip in IngredientChips) chip.IsSelected = wanted.Remove(chip.Name);
        // Ingredients not among the chips (older custom ones) get a chip of their own.
        foreach (var name in wanted) IngredientChips.Add(new IngredientChip(name, true));
    }

    void ResetFoodDetails()
    {
        NewFoodPortion = FoodLog.NoPortion;
        CustomIngredient = "";
        NewFoodPhotoPath = null;
        SetIngredients([]);
    }

    [RelayCommand]
    void ToggleIngredient(IngredientChip? chip)
    {
        if (chip != null) chip.IsSelected = !chip.IsSelected;
    }

    [RelayCommand]
    void AddCustomIngredient()
    {
        var name = CustomIngredient.Trim();
        if (name.Length == 0) return;
        if (name.Length > FoodLog.MaxIngredientLength) name = name[..FoodLog.MaxIngredientLength];
        var chip = IngredientChips.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (chip == null) IngredientChips.Add(new IngredientChip(name, true));
        else chip.IsSelected = true;
        CustomIngredient = "";
    }

    public static string MealPhotoFolder => Path.Combine(FileSystem.AppDataDirectory, FoodLog.PhotoFolderName);

    static MediaPickerOptions MealPhotoOptions => new()
    {
        Title = "Meal photo",
        // Small enough to upload quickly, still clear enough to see what's in the bowl.
        MaximumWidth = 1600,
        MaximumHeight = 1600,
        CompressionQuality = 80,
        RotateImage = true,
    };

    [RelayCommand]
    Task TakeFoodPhotoAsync() => UiActions.RunAsync(Errors, "Taking meal photo", async () =>
    {
        if (!MediaPicker.Default.IsCaptureSupported) throw new InvalidOperationException("This phone can't take photos from the app.");
        var photo = await MediaPicker.Default.CapturePhotoAsync(MealPhotoOptions);
        if (photo != null) await KeepFoodPhotoAsync(photo);
    });

    [RelayCommand]
    Task ChooseFoodPhotoAsync() => UiActions.RunAsync(Errors, "Choosing meal photo", async () =>
    {
        var options = MealPhotoOptions;
        options.SelectionLimit = 1;
        var photos = await MediaPicker.Default.PickPhotosAsync(options);
        var photo = photos?.FirstOrDefault();
        if (photo != null) await KeepFoodPhotoAsync(photo);
    });

    [RelayCommand]
    void RemoveFoodPhoto()
    {
        DiscardUnsavedPhoto();
        NewFoodPhotoPath = null;
    }

    /// <summary>Copies the photo into the app's private storage; it goes to Drive with the next export.</summary>
    async Task KeepFoodPhotoAsync(FileResult photo)
    {
        Directory.CreateDirectory(MealPhotoFolder);
        var extension = Path.GetExtension(photo.FileName);
        if (string.IsNullOrEmpty(extension)) extension = ".jpg";
        var path = Path.Combine(MealPhotoFolder, $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        await using (var source = await photo.OpenReadAsync())
        await using (var target = File.Create(path))
            await source.CopyToAsync(target);
        DiscardUnsavedPhoto();
        NewFoodPhotoPath = path;
    }

    /// <summary>Deletes a photo taken for the form that no saved entry uses.</summary>
    void DiscardUnsavedPhoto()
    {
        var path = NewFoodPhotoPath;
        if (string.IsNullOrEmpty(path) || path == EditingFood?.PhotoPath) return;
        TryDelete(path);
    }

    static void TryDelete(string? path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch { /* a leftover photo file is harmless */ }
    }

    /// <summary>Foods logged most often recently, most frequent first. Rebuilt after every add, edit or delete.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFrequentFoods))]
    public partial ObservableCollection<FrequentFood> FrequentFoodsList { get; set; } = [];
    public bool HasFrequentFoods => FrequentFoodsList.Count > 0;

    /// <summary>The quick pick behind the current text, so its protein is kept if the text isn't changed.</summary>
    FrequentFood? pickedFood;

    /// <summary>Tap a frequent food to fill in a new entry with it, timed now.</summary>
    [RelayCommand]
    void PickFrequentFood(FrequentFood? food)
    {
        if (food == null) return;
        DiscardUnsavedPhoto();
        EditingFood = null;
        NewFoodText = food.Text;
        NewFoodTime = ClockNow.Time(DateTime.Now);
        pickedFood = food;
        // Same food, same usual portion and ingredients; a photo is of this meal only, so it starts empty.
        NewFoodPhotoPath = null;
        CustomIngredient = "";
        NewFoodPortion = FoodLog.ChoiceFor(food.Portion);
        SetIngredients(food.Ingredients);
    }

    async Task LoadFrequentFoodsAsync()
    {
        var (from, to) = FrequentFoods.Window(DateTime.Now);
        var entries = await Store.GetFoodAsync(from, to);
        FrequentFoodsList = new ObservableCollection<FrequentFood>(FrequentFoods.Build(entries));
        var selected = SelectedIngredients;
        IngredientChips = new ObservableCollection<IngredientChip>(
            FoodLog.IngredientChoices(entries).Select(name => new IngredientChip(name)));
        SetIngredients(selected);
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
            await LoadPlanAsync(day);
            MobilityLabel = day.DayOfWeek == DayOfWeek.Sunday ? "Sunday mobility routine done" : "Mobility routine done";

            var c = await Store.GetCheckInAsync(day);
            CaffeineCups = c.CaffeineCups;
            HasLastCaffeine = c.LastCaffeineTime.HasValue;
            LastCaffeineTime = c.LastCaffeineTime ?? new TimeSpan(12, 0, 0);
            WaterOunces = WaterLog.Ounces(c);
            waterAdds.Clear();
            CanUndoWater = false;
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
            await LoadFrequentFoodsAsync();
            NewFoodTime = ClockNow.Time(DateTime.Now);

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
        LastCaffeineTime = ClockNow.Time(DateTime.Now);
    }

    [RelayCommand]
    void MealNow()
    {
        HasFirstMeal = true;
        FirstMealTime = ClockNow.Time(DateTime.Now);
    }

    [RelayCommand]
    void FoodTimeNow() => NewFoodTime = ClockNow.Time(DateTime.Now);

    [RelayCommand]
    void CaffeineTimeNow()
    {
        HasLastCaffeine = true;
        LastCaffeineTime = ClockNow.Time(DateTime.Now);
    }

    [RelayCommand]
    void MealTimeNow()
    {
        HasFirstMeal = true;
        FirstMealTime = ClockNow.Time(DateTime.Now);
    }

    async Task LoadMealsAsync(DateOnly day)
    {
        var meals = await Store.GetFoodAsync(day, day);
        Meals = new ObservableCollection<FoodEntry>(meals);
        MealsSummary = meals.Count == 0
            ? "Nothing logged yet. Write what you ate with rough portions, e.g. \"3 eggs, 1 slice toast, latte\"."
            : $"{meals.Count} entr{(meals.Count == 1 ? "y" : "ies")}"  + ". Tap one to edit or delete it.";
    }

    DailyCheckIn BuildCheckIn()
    {
        var checkIn = BuildCheckInFields();
        WaterLog.Set(checkIn, WaterOunces);
        return checkIn;
    }

    DailyCheckIn BuildCheckInFields() => new()
    {
        Date = DailyCheckIn.Key(DateOnly.FromDateTime(Date)),
        CaffeineCups = (int)CaffeineCups,
        LastCaffeineTime = HasLastCaffeine ? LastCaffeineTime : null,
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
        var oldPhoto = entry.PhotoPath;
        entry.Time = Date.Date + NewFoodTime;
        entry.Text = NewFoodText;
        entry.Portion = FoodLog.PortionFromChoice(NewFoodPortion);
        entry.IngredientList = SelectedIngredients;
        if (EditingFood == null && pickedFood?.ProteinGrams is { } protein
            && FrequentFoods.Key(NewFoodText) == FrequentFoods.Key(pickedFood.Text))
            entry.ProteinGrams = protein;
        if (NewFoodPhotoPath != oldPhoto)
        {
            entry.PhotoPath = NewFoodPhotoPath;
            entry.PhotoName = NewFoodPhotoPath == null ? null : FoodLog.PhotoName(entry, Path.GetExtension(NewFoodPhotoPath));
            entry.PhotoDriveId = null;
        }
        await Store.SaveFoodAsync(entry);
        if (oldPhoto != entry.PhotoPath) TryDelete(oldPhoto);
        EditingFood = null;
        NewFoodText = "";
        pickedFood = null;
        ResetFoodDetails();
        await LoadMealsAsync(DateOnly.FromDateTime(Date));
        await LoadFrequentFoodsAsync();
        await ApplyMealsAsync();
        // Send a new photo to Drive now rather than at the next scheduled export.
        if (entry.HasPhoto && string.IsNullOrEmpty(entry.PhotoDriveId)) _ = Exporter.ExportIfDueAsync(force: true);
    });

    /// <summary>Tap an entry to edit or delete it.</summary>
    [RelayCommand]
    Task FoodTappedAsync(FoodEntry? entry) => UiActions.RunAsync(Errors, "Changing food", async () =>
    {
        if (entry == null) return;
        var choice = await Shell.Current.DisplayActionSheetAsync(entry.Text, "Cancel", "Delete", "Edit");
        if (choice == "Edit")
        {
            DiscardUnsavedPhoto();
            EditingFood = entry;
            pickedFood = null;
            NewFoodText = entry.Text;
            NewFoodTime = entry.Time.TimeOfDay;
            NewFoodPortion = FoodLog.ChoiceFor(entry.Portion);
            CustomIngredient = "";
            NewFoodPhotoPath = entry.PhotoPath;
            SetIngredients(entry.IngredientList);
        }
        else if (choice == "Delete")
        {
            await Store.DeleteFoodAsync(entry.Id);
            if (EditingFood?.Id == entry.Id) CancelFoodEdit();
            // The phone's copy goes; a copy already uploaded stays in Drive's meal-photos folder.
            TryDelete(entry.PhotoPath);
            await LoadMealsAsync(DateOnly.FromDateTime(Date));
            await LoadFrequentFoodsAsync();
            await ApplyMealsAsync();
        }
    });

    [RelayCommand]
    Task SaveAsync() => UiActions.RunAsync(Errors, "Saving check-in", () => SaveCheckInAsync(BuildCheckIn()));
}
