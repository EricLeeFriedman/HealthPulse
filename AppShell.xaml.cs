using HealthPulse.Views;

namespace HealthPulse;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("migraine", typeof(MigraineEditPage));
        Routing.RegisterRoute("workout", typeof(WorkoutEditPage));
        Routing.RegisterRoute("measure", typeof(MeasurePage));
    }
}
