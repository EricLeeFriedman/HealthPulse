using HealthPulse.ViewModels;

namespace HealthPulse.Views;

/// <summary>Shell passes the "id" query to the view model (it implements IQueryAttributable).</summary>
public partial class WorkoutEditPage : ContentPage
{
    readonly WorkoutEditViewModel viewModel;

    public WorkoutEditPage(WorkoutEditViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync(null);
    }
}
