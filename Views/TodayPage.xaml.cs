using HealthPulse.ViewModels;

namespace HealthPulse.Views;

public partial class TodayPage : ContentPage
{
    readonly TodayViewModel viewModel;

    public TodayPage(TodayViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }
}
