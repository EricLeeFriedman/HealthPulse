using HealthPulse.ViewModels;

namespace HealthPulse.Views;

public partial class MeasurePage : ContentPage
{
    public MeasurePage(MeasureViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
