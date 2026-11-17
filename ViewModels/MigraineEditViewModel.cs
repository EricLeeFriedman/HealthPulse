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

    public List<string> ReliefOptions { get; } = ["Not yet known", .. MigraineEntry.ReliefOptions];

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
        var now = DateTime.Now;
        StartDate = source.StartTime.Date;
        StartClock = source.StartTime.TimeOfDay;
        IsOngoing = source.EndTime == null;
        var end = source.EndTime ?? now;
        EndDate = end.Date;
        EndClock = end.TimeOfDay;
        Severity = source.PeakSeverity;
        Aura = source.Aura;
        Nausea = source.Nausea;
        LightSensitivity = source.LightSensitivity;
        SoundSensitivity = source.SoundSensitivity;
        TriptanTaken = source.TriptanTaken;
        var triptan = source.TriptanTime ?? now;
        TriptanDate = triptan.Date;
        TriptanClock = triptan.TimeOfDay;
        ReliefIndex = source.TriptanRelief == null
            ? 0
            : Math.Max(0, Array.IndexOf(MigraineEntry.ReliefOptions, source.TriptanRelief) + 1);
        OtherMeds = source.OtherMeds ?? "";
        Notes = source.Notes ?? "";

        Triggers.Clear();
        var selected = source.TriggerList.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in MigraineEntry.TriggerOptions.Concat(selected.Except(MigraineEntry.TriggerOptions)))
            Triggers.Add(new TriggerOption(name, selected.Contains(name)));
    }

    [RelayCommand]
    async Task SaveAsync()
    {
        entry.StartTime = StartDate.Date + StartClock;
        entry.EndTime = IsOngoing ? null : EndDate.Date + EndClock;
        entry.PeakSeverity = (int)Math.Round(Severity);
        entry.Aura = Aura;
        entry.Nausea = Nausea;
        entry.LightSensitivity = LightSensitivity;
        entry.SoundSensitivity = SoundSensitivity;
        entry.TriptanTaken = TriptanTaken;
        entry.TriptanTime = TriptanTaken ? TriptanDate.Date + TriptanClock : null;
        entry.TriptanRelief = TriptanTaken && ReliefIndex > 0 ? MigraineEntry.ReliefOptions[ReliefIndex - 1] : null;
        entry.OtherMeds = string.IsNullOrWhiteSpace(OtherMeds) ? null : OtherMeds.Trim();
        entry.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        entry.TriggerList = Triggers.Where(t => t.IsSelected).Select(t => t.Name).ToList();

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
