using HealthPulse.ViewModels;

namespace HealthPulse.Views;

/// <summary>Shell passes the "id" query to the view model (it implements IQueryAttributable).</summary>
public partial class MigraineEditPage : ContentPage
{
    public MigraineEditPage(MigraineEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
