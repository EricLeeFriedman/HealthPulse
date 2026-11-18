using System.Globalization;
using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>One saved measurement or fitness test, as listed on the Measure screen for editing.</summary>
public record MeasureHistoryItem(string Kind, string Id, DateTime Date, string Display)
{
    public const string Body = "body";
    public const string Test = "test";
}

public static class MeasureHistory
{
    /// <summary>Measurements and fitness tests together, newest first.</summary>
    public static List<MeasureHistoryItem> Build(IEnumerable<BodyMeasurement> measurements, IEnumerable<FitnessTest> tests)
    {
        var items = measurements.Select(m => new MeasureHistoryItem(MeasureHistoryItem.Body, m.Id, m.Date,
                $"{Day(m.Date)}  •  " + Join(
                    m.WaistInches.HasValue ? $"waist {m.WaistInches.Value.ToString("0.##", CultureInfo.CurrentCulture)} in" : null,
                    m.WeightLbs.HasValue ? $"{m.WeightLbs.Value.ToString("0.#", CultureInfo.CurrentCulture)} lb" : null,
                    m.PhotoList.Count > 0 ? $"{m.PhotoList.Count} photo{(m.PhotoList.Count == 1 ? "" : "s")}" : null)))
            .Concat(tests.Select(t => new MeasureHistoryItem(MeasureHistoryItem.Test, t.Id, t.Date,
                $"{Day(t.Date)}  •  test: " + Join(
                    t.ToeTouchInches.HasValue ? $"toe touch {t.ToeTouchInches.Value.ToString("+0.#;-0.#;0", CultureInfo.CurrentCulture)} in" : null,
                    t.PushUps.HasValue ? $"{t.PushUps} push-ups" : null,
                    t.PlankSeconds.HasValue ? $"plank {t.PlankSeconds} s" : null))));
        return items.OrderByDescending(i => i.Date).ToList();
    }

    static string Day(DateTime date) => date.ToString("ddd MMM d", CultureInfo.CurrentCulture);

    static string Join(params string?[] parts)
    {
        var text = string.Join(", ", parts.Where(p => p != null));
        return text.Length == 0 ? "no values" : text;
    }
}
