using HealthPulse.ViewModels;

namespace HealthPulse.Views;

public partial class HealthPage : ContentPage
{
    readonly HealthViewModel viewModel;

    public HealthPage(HealthViewModel viewModel)
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
