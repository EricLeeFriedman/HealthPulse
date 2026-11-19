using System.Globalization;
using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>Targets, validation and reminders for the daily and monthly trackers.</summary>
public static class TrackerRules
{
    public const int ProteinMinGrams = 120;
    public const int ProteinMaxGrams = 150;

    public enum ProteinStatus { NotLogged, Below, OnTarget, Above }

    public static ProteinStatus Protein(int? grams) => grams switch
    {
        null => ProteinStatus.NotLogged,
        < ProteinMinGrams => ProteinStatus.Below,
        > ProteinMaxGrams => ProteinStatus.Above,
        _ => ProteinStatus.OnTarget
    };

    public static string ProteinText(int? grams) => Protein(grams) switch
    {
        ProteinStatus.NotLogged => $"Target {ProteinMinGrams} to {ProteinMaxGrams} g",
        ProteinStatus.Below => $"{ProteinMinGrams - grams} g short of {ProteinMinGrams} g",
        ProteinStatus.Above => $"Above the {ProteinMinGrams} to {ProteinMaxGrams} g target",
        _ => $"On target ({ProteinMinGrams} to {ProteinMaxGrams} g)"
    };

    /// <summary>Throws ArgumentException when a check-in has impossible values.</summary>
    public static void Validate(DailyCheckIn checkIn)
    {
        if (string.IsNullOrWhiteSpace(checkIn.Date)
            || !DateOnly.TryParseExact(checkIn.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("Check-in needs a date.");
        if (checkIn.CaffeineCups is < 0 or > 20) throw new ArgumentException("Caffeine must be 0 to 20 cups.");
        if (checkIn.WaterGlasses < 0 || WaterLog.Ounces(checkIn) is < 0 or > WaterLog.MaxOunces)
            throw new ArgumentException($"Water must be 0 to {WaterLog.MaxOunces} oz.");
        if (checkIn.AlcoholDrinks is < 0 or > 20) throw new ArgumentException("Alcohol must be 0 to 20 drinks.");
        if (checkIn.Stress is < 1 or > 5) throw new ArgumentException("Stress must be 1 to 5.");
        if (checkIn.ProteinGrams is < 0 or > 500) throw new ArgumentException("Protein must be 0 to 500 g.");
        if (checkIn.LastCaffeineTime is { } caffeine && (caffeine < TimeSpan.Zero || caffeine >= TimeSpan.FromDays(1)))
            throw new ArgumentException("Last caffeine time must be within the day.");
        if (checkIn.FirstMealTime is { } meal && (meal < TimeSpan.Zero || meal >= TimeSpan.FromDays(1)))
            throw new ArgumentException("First meal time must be within the day.");
    }

    public static void Validate(BodyMeasurement measurement)
    {
        if (measurement.WaistInches is < 15 or > 80) throw new ArgumentException("Waist must be 15 to 80 inches.");
        if (measurement.WeightLbs is < 50 or > 600) throw new ArgumentException("Weight must be 50 to 600 lb.");
        if (measurement.WaistInches == null && measurement.WeightLbs == null && measurement.PhotoList.Count == 0)
            throw new ArgumentException("Enter a waist, a weight or a photo.");
    }

    public static void Validate(FitnessTest test)
    {
        if (test.ToeTouchInches is < -30 or > 30) throw new ArgumentException("Toe-touch reach must be -30 to 30 inches.");
        if (test.PushUps is < 0 or > 500) throw new ArgumentException("Push-ups must be 0 to 500.");
        if (test.PlankSeconds is < 0 or > 3600) throw new ArgumentException("Plank must be 0 to 3600 seconds.");
        if (test.ToeTouchInches == null && test.PushUps == null && test.PlankSeconds == null)
            throw new ArgumentException("Enter at least one test result.");
    }

    public static void Validate(WorkoutSession session)
    {
        if (string.IsNullOrWhiteSpace(session.SessionType)) throw new ArgumentException("Pick a session type.");
        if (session.DurationMinutes is < 0 or > 600) throw new ArgumentException("Duration must be 0 to 600 minutes.");
        if (session.DistanceMiles is < 0 or > 100) throw new ArgumentException("Distance must be 0 to 100 miles.");
        foreach (var set in session.Sets)
        {
            if (string.IsNullOrWhiteSpace(set.Exercise)) throw new ArgumentException("Every set needs an exercise.");
            if (set.Reps is < 0 or > 1000) throw new ArgumentException($"{set.Exercise}: reps must be 0 to 1000.");
            if (set.WeightLbs is < 0 or > 500) throw new ArgumentException($"{set.Exercise}: weight must be 0 to 500 lb.");
            if (set.Seconds is < 0 or > 3600) throw new ArgumentException($"{set.Exercise}: time must be 0 to 3600 seconds.");
        }
    }

    /// <summary>Waist and weight are weekly: due when there's no measurement in the last 7 days.</summary>
    public static bool IsWeeklyMeasurementDue(DateOnly? lastMeasured, DateOnly today) =>
        lastMeasured == null || today.DayNumber - lastMeasured.Value.DayNumber >= 7;

    /// <summary>Photos and fitness tests are monthly: due when nothing was logged this calendar month.</summary>
    public static bool IsMonthlyDue(DateOnly? lastDone, DateOnly today) =>
        lastDone == null || lastDone.Value.Year != today.Year || lastDone.Value.Month != today.Month;

    /// <summary>Parses an optional whole number typed by the user. Blank → null.</summary>
    public static int? ParseInt(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            || int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return value;
        throw new ArgumentException($"{field} must be a whole number.");
    }

    /// <summary>Parses an optional decimal typed by the user. Blank → null.</summary>
    public static double? ParseDouble(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            || double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return value;
        throw new ArgumentException($"{field} must be a number.");
    }
}
