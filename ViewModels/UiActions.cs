using HealthPulse.Services;

namespace HealthPulse.ViewModels;

/// <summary>Runs a user action, records failures and shows them instead of crashing.</summary>
public static class UiActions
{
    public static async Task<bool> RunAsync(ErrorLog errors, string what, Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ArgumentException exception)
        {
            await Shell.Current.DisplayAlertAsync("Check your entry", exception.Message, "OK");
            return false;
        }
        catch (Exception exception)
        {
            errors.Record(what, exception);
            await Shell.Current.DisplayAlertAsync($"{what} failed", exception.Message, "OK");
            return false;
        }
    }
}
