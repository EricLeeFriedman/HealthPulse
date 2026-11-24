using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>A rough portion size, for meals without exact measurements.</summary>
public record Portion(string Name, string Size);

/// <summary>Rules for the free-text food log.</summary>
public static class FoodLog
{
    public const int MaxTextLength = 500;
    public const int MaxIngredients = 20;
    public const int MaxIngredientLength = 40;
    public const string PhotoFolderName = "meal-photos";

    /// <summary>
    /// Portion choices, each with the size it stands for so whoever reads the export
    /// (a person or an AI) can turn it into amounts.
    /// </summary>
    public static IReadOnlyList<Portion> Portions { get; } =
    [
        new("Small bowl", "about 1 cup"),
        new("Medium bowl", "about 1.5 to 2 cups"),
        new("Large bowl", "about 2.5 to 3 cups"),
        new("Cup", "1 measuring cup (8 fl oz)"),
        new("Fist", "about 1 cup"),
        new("Palm", "a palm-sized piece, about 3 to 4 oz cooked meat or fish"),
        new("Handful", "about 1 oz, e.g. nuts or chips"),
        new("Plate", "a full dinner plate (about 10 in)"),
    ];

    public const string NoPortion = "No portion";

    /// <summary>The portion picker's options: no portion first, then each portion with its size.</summary>
    public static IReadOnlyList<string> PortionChoices { get; } =
        [NoPortion, .. Portions.Select(p => $"{p.Name} ({p.Size})")];

    /// <summary>The portion name for a picker choice, or null for no portion.</summary>
    public static string? PortionFromChoice(string? choice) =>
        string.IsNullOrWhiteSpace(choice) || choice == NoPortion
            ? null
            : Portions.FirstOrDefault(p => choice.StartsWith(p.Name + " (", StringComparison.Ordinal))?.Name;

    /// <summary>The picker choice for a stored portion name.</summary>
    public static string ChoiceFor(string? portion) =>
        FindPortion(portion) is { } p ? $"{p.Name} ({p.Size})" : NoPortion;

    public static Portion? FindPortion(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : Portions.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Ingredient chips offered on every entry, protein sources first.</summary>
    public static IReadOnlyList<string> CommonIngredients { get; } =
    [
        "Chicken", "Beef", "Pork", "Turkey", "Fish", "Shrimp", "Eggs", "Tofu",
        "Beans", "Lentils", "Cheese", "Yogurt", "Milk", "Protein powder", "Nuts",
        "Rice", "Pasta", "Bread", "Potatoes", "Veggies",
    ];

    public const int MaxCustomChips = 10;

    /// <summary>
    /// The ingredient chips to show: the common ones, then ingredients typed in on recent entries
    /// that aren't common, most used first.
    /// </summary>
    public static List<string> IngredientChoices(IEnumerable<FoodEntry> recent)
    {
        var common = new HashSet<string>(CommonIngredients, StringComparer.OrdinalIgnoreCase);
        var custom = recent
            .SelectMany(e => e.IngredientList.Select(i => (Ingredient: i, e.Time)))
            .Where(x => !common.Contains(x.Ingredient))
            .GroupBy(x => x.Ingredient, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(x => x.Time))
            .Select(g => g.MaxBy(x => x.Time).Ingredient)
            .Take(MaxCustomChips);
        return [.. CommonIngredients, .. custom];
    }

    /// <summary>
    /// The header of the collapsible ingredient section, e.g. "▸ What's in it (2 ticked)", so ticked
    /// ingredients (from a frequent food or an entry being edited) are visible while it's collapsed.
    /// </summary>
    public static string IngredientsHeader(bool expanded, int selectedCount) =>
        (expanded ? "▾ " : "▸ ") + "What's in it" +
        (selectedCount > 0 ? $" ({selectedCount} ticked)" : expanded ? " (tap all that apply)" : " (optional)");

    /// <summary>The Drive file name for a meal photo, e.g. "meal-2026-11-20-1230-1a2b3c4d.jpg".</summary>
    public static string PhotoName(FoodEntry entry, string extension)
    {
        extension = string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension.ToLowerInvariant();
        if (!extension.StartsWith('.')) extension = "." + extension;
        var id = new string(entry.Id.Where(char.IsLetterOrDigit).Take(8).ToArray());
        return $"meal-{entry.Time:yyyy-MM-dd-HHmm}-{id}{extension}";
    }

    public static string PhotoMimeType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            _ => "image/jpeg",
        };

    public static void Validate(FoodEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Text)) throw new ArgumentException("Describe what you ate.");
        if (entry.Text.Trim().Length > MaxTextLength) throw new ArgumentException($"Keep it under {MaxTextLength} characters.");
        if (entry.ProteinGrams is < 0 or > 300) throw new ArgumentException("Protein must be 0 to 300 g.");
        if (!string.IsNullOrWhiteSpace(entry.Portion) && FindPortion(entry.Portion) == null)
            throw new ArgumentException($"Unknown portion \"{entry.Portion}\".");
        var ingredients = entry.IngredientList;
        if (ingredients.Count > MaxIngredients) throw new ArgumentException($"Pick at most {MaxIngredients} ingredients.");
        if (ingredients.Any(i => i.Length > MaxIngredientLength))
            throw new ArgumentException($"Keep each ingredient under {MaxIngredientLength} characters.");
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
