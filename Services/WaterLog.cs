using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>A one-tap water amount on the Today tab.</summary>
public record WaterAmount(string Label, int Ounces);

/// <summary>
/// Water is logged in fluid ounces. Days saved before ounces existed only have a glass count;
/// each of those glasses counts as 8 oz, so old days still add up against the target.
/// </summary>
public static class WaterLog
{
    public const int OuncesPerGlass = 8;
    /// <summary>Daily target: 80 oz (ten 8 oz glasses).</summary>
    public const int TargetOunces = 80;
    public const int MaxOunces = 400;
    /// <summary>A large Stanley tumbler holds about 40 oz.</summary>
    public const int StanleyOunces = 40;

    /// <summary>Quick buttons, smallest first.</summary>
    public static readonly IReadOnlyList<WaterAmount> QuickAmounts =
    [
        new("Glass 8 oz", OuncesPerGlass),
        new("¼ Stanley 10 oz", StanleyOunces / 4),
        new("½ Stanley 20 oz", StanleyOunces / 2),
        new("Stanley 40 oz", StanleyOunces),
    ];

    /// <summary>The day's water in ounces; a day saved as glasses only counts 8 oz per glass.</summary>
    public static int Ounces(DailyCheckIn checkIn) =>
        checkIn.WaterOunces ?? checkIn.WaterGlasses * OuncesPerGlass;

    /// <summary>Stores ounces, and keeps the glass count as the nearest whole number of 8 oz glasses.</summary>
    public static void Set(DailyCheckIn checkIn, int ounces)
    {
        checkIn.WaterOunces = ounces;
        checkIn.WaterGlasses = Glasses(ounces);
    }

    public static int Glasses(int ounces) => (int)Math.Round(ounces / (double)OuncesPerGlass, MidpointRounding.AwayFromZero);

    /// <summary>Adds (or, with a negative amount, removes) water, never going below 0 or above the maximum.</summary>
    public static int Add(int total, int ounces) => Math.Clamp(total + ounces, 0, MaxOunces);

    public static string Text(int ounces) => $"Water: {ounces} of {TargetOunces} oz";

    public static string Status(int ounces) => ounces >= TargetOunces
        ? "Target reached"
        : $"{TargetOunces - ounces} oz to go";

    /// <summary>Parses a typed amount of ounces (decimals are rounded). Throws ArgumentException when it isn't usable.</summary>
    public static int ParseOunces(string? text)
    {
        var value = TrackerRules.ParseDouble(text, "Ounces")
            ?? throw new ArgumentException("Enter how many ounces, e.g. 16.");
        var ounces = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        if (ounces <= 0 || ounces > MaxOunces) throw new ArgumentException($"Enter 1 to {MaxOunces} oz.");
        return ounces;
    }
}
