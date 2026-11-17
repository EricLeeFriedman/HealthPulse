using HealthPulse.ViewModels;

namespace HealthPulse.Views;

public partial class MigrainesPage : ContentPage
{
    readonly MigrainesViewModel viewModel;

    public MigrainesPage(MigrainesViewModel viewModel)
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
