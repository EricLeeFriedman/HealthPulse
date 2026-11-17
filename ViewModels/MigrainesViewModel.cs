using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.ViewModels;

public partial class MigrainesViewModel : ObservableObject
{
    public IMigraineService Migraines;
    public HealthExportService Exporter;

    [ObservableProperty]
    public partial ObservableCollection<MigraineEntry> Entries { get; set; } = [];

    [ObservableProperty]
    public partial MigraineEntry? Ongoing { get; set; }

    [ObservableProperty]
    public partial string StatsText { get; set; } = "";

    public bool HasOngoing => Ongoing != null;
    public bool NoOngoing => Ongoing == null;
    public bool HasEntries => Entries.Count > 0;
    public bool NoEntries => Entries.Count == 0;
    public string OngoingText => Ongoing == null
        ? ""
        : $"Started {Ongoing.StartTime:h:mm tt} ({(DateTime.Now - Ongoing.StartTime).TotalHours:0.#} h ago), pain {Ongoing.PeakSeverity}/10"
          + (Ongoing.TriptanTaken ? $", triptan at {Ongoing.TriptanTime:h:mm tt}" : "");
    public bool OngoingNeedsTriptan => Ongoing is { TriptanTaken: false };

    public MigrainesViewModel(IMigraineService migraines, HealthExportService exporter)
    {
        Migraines = migraines;
        Exporter = exporter;
    }

    public async Task LoadAsync()
    {
        var entries = await Migraines.GetEntriesAsync();
        Entries = new ObservableCollection<MigraineEntry>(entries);
        Ongoing = entries.FirstOrDefault(e => e.IsOngoing);
        StatsText = BuildStats(entries, DateTime.Now);
        OnPropertyChanged(nameof(HasOngoing));
        OnPropertyChanged(nameof(NoOngoing));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(NoEntries));
        OnPropertyChanged(nameof(OngoingText));
        OnPropertyChanged(nameof(OngoingNeedsTriptan));
    }

    public static string BuildStats(IEnumerable<MigraineEntry> entries, DateTime now)
    {
        var cutoff = now.AddDays(-30);
        var recent = entries.Where(e => e.StartTime >= cutoff).ToList();
        var migraineDays = recent.Select(e => e.StartTime.Date).Distinct().Count();
        var triptanDays = recent.Where(e => e.TriptanTaken)
            .Select(e => (e.TriptanTime ?? e.StartTime).Date).Distinct().Count();
        return $"Last 30 days: {recent.Count} attacks on {migraineDays} days • triptan on {triptanDays} days";
    }

    [RelayCommand]
    async Task LogNewAsync() => await Shell.Current.GoToAsync("migraine");

    [RelayCommand]
    async Task OpenAsync(MigraineEntry? entry)
    {
        if (entry == null) return;
        await Shell.Current.GoToAsync($"migraine?id={Uri.EscapeDataString(entry.Id)}");
    }

    [RelayCommand]
    async Task EndOngoingAsync()
    {
        if (Ongoing == null) return;
        Ongoing.EndTime = DateTime.Now;
        await SaveAndRefreshAsync(Ongoing);
    }

    [RelayCommand]
    async Task TriptanNowAsync()
    {
        if (Ongoing == null) return;
        Ongoing.TriptanTaken = true;
        Ongoing.TriptanTime = DateTime.Now;
        await SaveAndRefreshAsync(Ongoing);
    }

    async Task SaveAndRefreshAsync(MigraineEntry entry)
    {
        await Migraines.SaveEntryAsync(entry);
        await LoadAsync();
        _ = Exporter.ExportIfDueAsync(force: true);
    }
}
