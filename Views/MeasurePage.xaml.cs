using HealthPulse.ViewModels;

namespace HealthPulse.Views;

public partial class MeasurePage : ContentPage
{
    readonly MeasureViewModel viewModel;

    public MeasurePage(MeasureViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadHistoryAsync();
    }
}
