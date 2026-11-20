using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>
/// Copies meal photos to My Drive/HealthPulse/meal-photos during an export, so whoever reads
/// the export (a person or an AI) can look at what was eaten. Each photo is uploaded once.
/// </summary>
public static class MealPhotos
{
    /// <summary>Photos uploaded per export at most, so one export can't run long; the rest go next time.</summary>
    public const int MaxPerExport = 10;

    /// <summary>Path of an uploaded photo relative to the export file's folder.</summary>
    public static string ExportPath(FoodEntry entry) => $"{FoodLog.PhotoFolderName}/{entry.PhotoName}";

    /// <summary>
    /// Uploads photos of entries in the window that have a local photo but no Drive copy yet.
    /// Stops at the first failure (usually no connection) and returns its message; null when all went up.
    /// </summary>
    /// <param name="readPhoto">Reads a local photo; null when the file is gone.</param>
    public static async Task<string?> UploadPendingAsync(ITrackerStore store, IExportDestination destination,
        Func<string, Task<byte[]?>> readPhoto, DateOnly from, DateOnly to)
    {
        if (!destination.HasDestination || !destination.CanUploadPhotos) return null;
        var pending = (await store.GetFoodAsync(from, to))
            .Where(e => e.HasPhoto && string.IsNullOrEmpty(e.PhotoDriveId))
            .OrderByDescending(e => e.Time)
            .Take(MaxPerExport)
            .ToList();
        foreach (var entry in pending)
        {
            try
            {
                var bytes = await readPhoto(entry.PhotoPath!);
                if (bytes == null || bytes.Length == 0) continue;
                if (string.IsNullOrEmpty(entry.PhotoName))
                    entry.PhotoName = FoodLog.PhotoName(entry, Path.GetExtension(entry.PhotoPath!));
                entry.PhotoDriveId = await destination.UploadPhotoAsync(
                    entry.PhotoName!, bytes, FoodLog.PhotoMimeType(entry.PhotoName!));
                await store.SaveFoodAsync(entry);
            }
            catch (Exception exception)
            {
                return $"Meal photos: {exception.Message}";
            }
        }
        return null;
    }
}
