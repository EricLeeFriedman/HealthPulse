using System.Diagnostics;

namespace HealthPulse.Services;

/// <summary>
/// Runs the steps of one Health Connect read within a time budget and notes how
/// each went, so a slow or stuck call shows up in the export instead of silently
/// losing all the data.
/// </summary>
public class HealthReadReport
{
    readonly Stopwatch watch = Stopwatch.StartNew();
    readonly object gate = new();
    readonly List<string> steps = [];

    public HealthReadReport(TimeSpan budget)
    {
        Budget = budget;
    }

    public TimeSpan Budget { get; }
    public bool OutOfTime => watch.Elapsed >= Budget;

    public IReadOnlyList<string> Steps
    {
        get { lock (gate) return steps.ToList(); }
    }

    public void Note(string text)
    {
        lock (gate) steps.Add(text);
    }

    /// <summary>Runs one step unless the budget is spent. Returns whether it succeeded.</summary>
    public async Task<bool> RunAsync(string name, Func<Task> step)
    {
        if (OutOfTime)
        {
            Note($"{name}: skipped, out of time");
            return false;
        }
        var timer = Stopwatch.StartNew();
        try
        {
            await step();
            Note($"{name}: ok in {timer.ElapsedMilliseconds} ms");
            return true;
        }
        catch (Exception exception)
        {
            Note($"{name}: failed after {timer.ElapsedMilliseconds} ms ({exception.GetType().Name}: {exception.Message})");
            return false;
        }
    }
}
