namespace HealthPulse.Services;

/// <summary>
/// Puts a time limit on work that talks to the system or the network, so a call
/// that never answers turns into a clear error instead of a stuck spinner.
/// </summary>
public static class Timeouts
{
    public static async Task<T> Limit<T>(Task<T> task, TimeSpan limit, string what)
    {
        try
        {
            return await task.WaitAsync(limit);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"{what} didn't finish within {Describe(limit)}.");
        }
    }

    public static async Task Limit(Task task, TimeSpan limit, string what)
    {
        try
        {
            await task.WaitAsync(limit);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"{what} didn't finish within {Describe(limit)}.");
        }
    }

    public static string Describe(TimeSpan limit) =>
        limit.TotalSeconds >= 120 ? $"{limit.TotalMinutes:0.#} minutes"
        : limit.TotalSeconds >= 1 ? $"{limit.TotalSeconds:0} seconds"
        : $"{limit.TotalMilliseconds:0} ms";
}
