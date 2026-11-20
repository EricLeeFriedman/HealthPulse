using System.Text.RegularExpressions;
using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>A food logged often, offered as a one-tap pick in the food log.</summary>
public class FrequentFood
{
    /// <summary>The text as most recently logged, e.g. "Chobani 6.7 oz".</summary>
    public string Text { get; init; } = "";

    /// <summary>Protein from the most recent entry that had an estimate, if any.</summary>
    public int? ProteinGrams { get; init; }

    /// <summary>Portion from the most recent entry, if it had one.</summary>
    public string? Portion { get; init; }

    /// <summary>Ingredients from the most recent entry that had any.</summary>
    public IReadOnlyList<string> Ingredients { get; init; } = [];

    /// <summary>Times logged in the look-back window.</summary>
    public int Count { get; init; }

    public DateTime LastEaten { get; init; }

    public string Display => Count > 1 ? $"{Text}  ×{Count}" : Text;
}

/// <summary>
/// Builds the frequent foods list from past entries: the same food (ignoring case, spacing and a
/// trailing period) is grouped, sorted by how often it was logged, then by how recently.
/// Recomputed from the log each time, so it follows what's actually being eaten.
/// </summary>
public static partial class FrequentFoods
{
    /// <summary>How far back entries count, so foods no longer eaten drop off.</summary>
    public const int LookBackDays = 90;
    public const int MaxItems = 8;

    public static string Key(string text) =>
        Spaces().Replace(text.Trim().TrimEnd('.').Trim(), " ").ToLowerInvariant();

    public static List<FrequentFood> Build(IEnumerable<FoodEntry> entries, int max = MaxItems) =>
        entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Text))
            .GroupBy(e => Key(e.Text))
            .Select(g =>
            {
                var latest = g.MaxBy(e => e.Time)!;
                return new FrequentFood
                {
                    Text = latest.Text.Trim(),
                    ProteinGrams = g.Where(e => e.ProteinGrams.HasValue).MaxBy(e => e.Time)?.ProteinGrams,
                    Portion = FoodLog.FindPortion(latest.Portion)?.Name,
                    Ingredients = g.Where(e => e.IngredientList.Count > 0).MaxBy(e => e.Time)?.IngredientList ?? [],
                    Count = g.Count(),
                    LastEaten = latest.Time,
                };
            })
            .OrderByDescending(f => f.Count)
            .ThenByDescending(f => f.LastEaten)
            .Take(max)
            .ToList();

    /// <summary>The range of days to read entries from, ending today.</summary>
    public static (DateOnly From, DateOnly To) Window(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        return (today.AddDays(-(LookBackDays - 1)), today);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
