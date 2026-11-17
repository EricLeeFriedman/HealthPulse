using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>Rules for the free-text food log.</summary>
public static class FoodLog
{
    public const int MaxTextLength = 500;

    public static void Validate(FoodEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Text)) throw new ArgumentException("Describe what you ate.");
        if (entry.Text.Trim().Length > MaxTextLength) throw new ArgumentException($"Keep it under {MaxTextLength} characters.");
        if (entry.ProteinGrams is < 0 or > 300) throw new ArgumentException("Protein must be 0 to 300 g.");
    }

    /// <summary>Sum of the meals' protein estimates, or null when none has one.</summary>
    public static int? ProteinTotal(IEnumerable<FoodEntry> entries)
    {
        var withProtein = entries.Where(e => e.ProteinGrams.HasValue).ToList();
        return withProtein.Count == 0 ? null : withProtein.Sum(e => e.ProteinGrams!.Value);
    }

    /// <summary>
    /// Updates a day's check-in from its meals: the protein total comes from the meals when
    /// any has an estimate, and the first meal time from the earliest entry unless an earlier one was set.
    /// Returns true when the check-in changed.
    /// </summary>
    public static bool ApplyTo(DailyCheckIn checkIn, IReadOnlyCollection<FoodEntry> dayEntries)
    {
        var changed = false;
        var protein = ProteinTotal(dayEntries);
        if (protein.HasValue && checkIn.ProteinGrams != protein)
        {
            checkIn.ProteinGrams = protein;
            changed = true;
        }
        if (dayEntries.Count > 0)
        {
            var first = dayEntries.Min(e => e.Time).TimeOfDay;
            first = new TimeSpan(first.Hours, first.Minutes, 0);
            // Keep an earlier first meal the user set by hand.
            if (checkIn.FirstMealTime == null || checkIn.FirstMealTime > first)
            {
                checkIn.FirstMealTime = first;
                changed = true;
            }
        }
        return changed;
    }
}
