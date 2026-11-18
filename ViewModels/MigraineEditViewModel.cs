using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.ViewModels;

public partial class TriggerOption : ObservableObject
{
    public TriggerOption(string name, bool selected)
    {
        Name = name;
        IsSelected = selected;
    }

    public string Name { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public partial class MigraineEditViewModel : ObservableObject, IQueryAttributable
{
    public IMigraineService Migraines;
    public HealthExportService Exporter;
    MigraineEntry entry = new();

    public MigraineEditViewModel(IMigraineService migraines, HealthExportService exporter)
    {
        Migraines = migraines;
        Exporter = exporter;
        LoadFrom(entry);
    }

    public List<string> ReliefOptions { get; } = [.. MigraineForm.ReliefChoices];

    [ObservableProperty] public partial bool IsExisting { get; set; }
    [ObservableProperty] public partial string PageTitle { get; set; } = "Log migraine";

    [ObservableProperty] public partial DateTime StartDate { get; set; }
    [ObservableProperty] public partial TimeSpan StartClock { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEnded))]
    public partial bool IsOngoing { get; set; }
    public bool HasEnded => !IsOngoing;
    [ObservableProperty] public partial DateTime EndDate { get; set; }
    [ObservableProperty] public partial TimeSpan EndClock { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeverityText))]
    public partial double Severity { get; set; }
    public string SeverityText => $"Peak pain: {(int)Math.Round(Severity)}/10";

    [ObservableProperty] public partial bool Aura { get; set; }
    [ObservableProperty] public partial bool Nausea { get; set; }
    [ObservableProperty] public partial bool LightSensitivity { get; set; }
    [ObservableProperty] public partial bool SoundSensitivity { get; set; }

    [ObservableProperty] public partial bool TriptanTaken { get; set; }
    [ObservableProperty] public partial DateTime TriptanDate { get; set; }
    [ObservableProperty] public partial TimeSpan TriptanClock { get; set; }
    [ObservableProperty] public partial int ReliefIndex { get; set; }

    [ObservableProperty] public partial string OtherMeds { get; set; } = "";
    [ObservableProperty] public partial string Notes { get; set; } = "";

    public ObservableCollection<TriggerOption> Triggers { get; } = [];

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("id", out var idValue) || idValue is not string id) return;
        var existing = await Migraines.GetEntryAsync(Uri.UnescapeDataString(id));
        if (existing == null) return;
        entry = existing;
        IsExisting = true;
        PageTitle = "Edit migraine";
        LoadFrom(existing);
    }

    void LoadFrom(MigraineEntry source)
    {
        var form = MigraineForm.FromEntry(source, DateTime.Now);
        StartDate = form.StartDate;
        StartClock = form.StartClock;
        IsOngoing = form.IsOngoing;
        EndDate = form.EndDate;
        EndClock = form.EndClock;
        Severity = form.Severity;
        Aura = form.Aura;
        Nausea = form.Nausea;
        LightSensitivity = form.LightSensitivity;
        SoundSensitivity = form.SoundSensitivity;
        TriptanTaken = form.TriptanTaken;
        TriptanDate = form.TriptanDate;
        TriptanClock = form.TriptanClock;
        ReliefIndex = form.ReliefIndex;
        OtherMeds = form.OtherMeds;
        Notes = form.Notes;
        Triggers.Clear();
        foreach (var (name, selected) in form.Triggers)
            Triggers.Add(new TriggerOption(name, selected));
    }

    MigraineForm ToForm() => new()
    {
        StartDate = StartDate,
        StartClock = StartClock,
        IsOngoing = IsOngoing,
        EndDate = EndDate,
        EndClock = EndClock,
        Severity = Severity,
        Aura = Aura,
        Nausea = Nausea,
        LightSensitivity = LightSensitivity,
        SoundSensitivity = SoundSensitivity,
        TriptanTaken = TriptanTaken,
        TriptanDate = TriptanDate,
        TriptanClock = TriptanClock,
        ReliefIndex = ReliefIndex,
        OtherMeds = OtherMeds,
        Notes = Notes,
        Triggers = Triggers.Select(t => (t.Name, t.IsSelected)).ToList()
    };

    [RelayCommand]
    void StartNow() => (StartDate, StartClock) = ClockNow.Stamp(DateTime.Now);

    [RelayCommand]
    void EndNow() => (EndDate, EndClock) = ClockNow.Stamp(DateTime.Now);

    [RelayCommand]
    void TriptanNow() => (TriptanDate, TriptanClock) = ClockNow.Stamp(DateTime.Now);

    [RelayCommand]
    async Task SaveAsync()
    {
        ToForm().ApplyTo(entry);

        try
        {
            await Migraines.SaveEntryAsync(entry);
        }
        catch (ArgumentException exception)
        {
            await Shell.Current.DisplayAlertAsync("Can't save", exception.Message, "OK");
            return;
        }

        _ = Exporter.ExportIfDueAsync(force: true);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    async Task DeleteAsync()
    {
        if (!IsExisting) return;
        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Delete migraine", "Delete this entry? This can't be undone.", "Delete", "Cancel");
        if (!confirmed) return;
        await Migraines.DeleteEntryAsync(entry.Id);
        _ = Exporter.ExportIfDueAsync(force: true);
        await Shell.Current.GoToAsync("..");
    }
}
