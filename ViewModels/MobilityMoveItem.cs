using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthPulse.Services;

namespace HealthPulse.ViewModels;

/// <summary>One mobility move, tap to show how to do it and a link to photos.</summary>
public partial class MobilityMoveItem : ObservableObject
{
    readonly MobilityMove move;

    public MobilityMoveItem(int number, MobilityMove move)
    {
        this.move = move;
        Title = $"{number}. {move.Name}";
    }

    public string Title { get; }
    public string Dose => move.Dose;
    public string How => move.How;
    public string DemoText => $"See photos ({move.DemoSource}) ›";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    public partial bool IsExpanded { get; set; }
    public string Chevron => IsExpanded ? "▾" : "▸";

    [RelayCommand]
    void Toggle() => IsExpanded = !IsExpanded;

    [RelayCommand]
    async Task OpenDemo()
    {
        try
        {
            await Browser.Default.OpenAsync(new Uri(move.DemoUrl), BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception exception)
        {
            await Shell.Current.DisplayAlertAsync("Couldn't open the link", exception.Message, "OK");
        }
    }

    public static List<MobilityMoveItem> All() =>
        WorkoutPlan.MobilityMoves.Select((m, i) => new MobilityMoveItem(i + 1, m)).ToList();
}
