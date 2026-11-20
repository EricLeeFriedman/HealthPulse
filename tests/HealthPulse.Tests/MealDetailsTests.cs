using System.Text.Json;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

/// <summary>Portion, ingredient chips and meal photos on food log entries.</summary>
public sealed class MealDetailsTests : IAsyncLifetime
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"meal-details-{Guid.NewGuid():N}.db3");
    LocalTrackerStore store = null!;

    public Task InitializeAsync()
    {
        store = new LocalTrackerStore(path);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await store.Database.CloseAsync();
        File.Delete(path);
    }

    static readonly DateTime Lunch = new(2026, 11, 20, 12, 30, 0);

    [Fact]
    public void Validate_AcceptsKnownPortionsAndRejectsOthers()
    {
        FoodLog.Validate(new FoodEntry { Text = "minestrone", Portion = "Large bowl", Ingredients = "Beans|Pasta" });
        FoodLog.Validate(new FoodEntry { Text = "minestrone", Portion = "medium BOWL" });
        Assert.Throws<ArgumentException>(() => FoodLog.Validate(new FoodEntry { Text = "soup", Portion = "Bucket" }));
        Assert.Throws<ArgumentException>(() => FoodLog.Validate(new FoodEntry { Text = "soup", Ingredients = new string('x', 41) }));
    }

    [Fact]
    public void PortionChoices_RoundTripThroughThePicker()
    {
        Assert.Equal(FoodLog.NoPortion, FoodLog.PortionChoices[0]);
        Assert.Equal(FoodLog.Portions.Count + 1, FoodLog.PortionChoices.Count);
        foreach (var portion in FoodLog.Portions)
            Assert.Equal(portion.Name, FoodLog.PortionFromChoice(FoodLog.ChoiceFor(portion.Name)));
        Assert.Equal("Medium bowl (about 1.5 to 2 cups)", FoodLog.ChoiceFor("Medium bowl"));
        Assert.Null(FoodLog.PortionFromChoice(FoodLog.NoPortion));
        Assert.Null(FoodLog.PortionFromChoice(null));
        Assert.Equal(FoodLog.NoPortion, FoodLog.ChoiceFor(null));
    }

    [Fact]
    public void IngredientList_TrimsAndDropsDuplicates()
    {
        var entry = new FoodEntry { IngredientList = [" Beans ", "beans", "", "Cheese"] };
        Assert.Equal("Beans|Cheese", entry.Ingredients);
        Assert.Equal(new[] { "Beans", "Cheese" }, entry.IngredientList);
        Assert.Empty(new FoodEntry().IngredientList);
    }

    [Fact]
    public void IngredientChoices_AddsRecentCustomIngredientsAfterCommonOnes()
    {
        var choices = FoodLog.IngredientChoices(
        [
            new FoodEntry { Time = Lunch, Text = "soup", Ingredients = "Beans|Ham" },
            new FoodEntry { Time = Lunch.AddDays(-1), Text = "soup", Ingredients = "ham|Kale" },
            new FoodEntry { Time = Lunch.AddDays(-2), Text = "eggs", Ingredients = "eggs" },
        ]);

        Assert.Equal(FoodLog.CommonIngredients, choices.Take(FoodLog.CommonIngredients.Count));
        Assert.Equal(new[] { "Ham", "Kale" }, choices.Skip(FoodLog.CommonIngredients.Count));
    }

    [Fact]
    public void Display_ShowsPortionIngredientsProteinAndPhoto()
    {
        var entry = new FoodEntry
        {
            Text = "minestrone", Portion = "Large bowl", Ingredients = "Beans|Cheese", ProteinGrams = 22,
            PhotoPath = "/data/meal.jpg",
        };
        Assert.Equal("minestrone  •  Large bowl  •  Beans, Cheese  •  22 g protein  •  📷", entry.Display);
        Assert.Equal("apple", new FoodEntry { Text = "apple" }.Display);
    }

    [Fact]
    public void PhotoName_IsDatedAndUnique()
    {
        var entry = new FoodEntry { Id = "1a2b3c4d-5e6f", Time = Lunch };
        Assert.Equal("meal-2026-11-20-1230-1a2b3c4d.jpg", FoodLog.PhotoName(entry, ".JPG"));
        Assert.Equal("meal-2026-11-20-1230-1a2b3c4d.jpg", FoodLog.PhotoName(entry, ""));
        Assert.Equal("image/png", FoodLog.PhotoMimeType("meal.png"));
        Assert.Equal("image/jpeg", FoodLog.PhotoMimeType("meal.jpg"));
    }

    [Fact]
    public void FrequentFoods_RememberPortionAndIngredients()
    {
        var food = Assert.Single(FrequentFoods.Build(
        [
            new FoodEntry { Time = Lunch.AddDays(-2), Text = "Minestrone", Portion = "Small bowl", Ingredients = "Beans|Cheese", ProteinGrams = 18 },
            new FoodEntry { Time = Lunch, Text = "minestrone", Portion = "Large bowl" },
        ]));
        Assert.Equal("Large bowl", food.Portion);
        Assert.Equal(new[] { "Beans", "Cheese" }, food.Ingredients);
        Assert.Equal(18, food.ProteinGrams);
    }

    [Fact]
    public async Task Store_KeepsPortionIngredientsAndPhoto()
    {
        var entry = new FoodEntry
        {
            Time = Lunch, Text = "minestrone", Portion = "Large bowl", Ingredients = "Beans|Cheese|Pasta",
            PhotoPath = "/data/meal-photos/x.jpg", PhotoName = "meal-2026-11-20-1230-abc.jpg",
        };
        await store.SaveFoodAsync(entry);

        var saved = Assert.Single(await store.GetFoodAsync(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20)));
        Assert.Equal("Large bowl", saved.Portion);
        Assert.Equal(new[] { "Beans", "Cheese", "Pasta" }, saved.IngredientList);
        Assert.Equal("/data/meal-photos/x.jpg", saved.PhotoPath);
        Assert.Equal("meal-2026-11-20-1230-abc.jpg", saved.PhotoName);
        Assert.Null(saved.PhotoDriveId);
    }

    [Fact]
    public void Export_IncludesPortionIngredientsAndPhoto()
    {
        var trackers = TrackerExport.Empty with
        {
            Food =
            [
                new FoodEntry
                {
                    Time = Lunch, Text = "minestrone", Portion = "Large bowl", Ingredients = "Beans|Cheese",
                    PhotoPath = "/data/a.jpg", PhotoName = "meal-2026-11-20-1230-aaaa.jpg", PhotoDriveId = "drive-1",
                },
                new FoodEntry { Time = Lunch.AddHours(6), Text = "chili", PhotoPath = "/data/b.jpg", PhotoName = "meal-b.jpg" },
                new FoodEntry { Time = Lunch.AddHours(8), Text = "apple" },
            ]
        };

        var json = HealthExportService.BuildJson(DateTime.UtcNow, "UTC",
            new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20), [], [], trackers: trackers);

        using var doc = JsonDocument.Parse(json);
        var meals = doc.RootElement.GetProperty("days")[0].GetProperty("meals");
        var soup = meals[0];
        Assert.Equal("Large bowl", soup.GetProperty("portion").GetString());
        Assert.Equal("about 2.5 to 3 cups", soup.GetProperty("portionSize").GetString());
        Assert.Equal(new[] { "Beans", "Cheese" }, soup.GetProperty("ingredients").EnumerateArray().Select(i => i.GetString()));
        Assert.Equal("meal-photos/meal-2026-11-20-1230-aaaa.jpg", soup.GetProperty("photo").GetString());
        Assert.Equal("drive-1", soup.GetProperty("photoDriveFileId").GetString());
        Assert.False(soup.TryGetProperty("photoPendingUpload", out _));

        var chili = meals[1];
        Assert.False(chili.TryGetProperty("photo", out _));
        Assert.True(chili.GetProperty("photoPendingUpload").GetBoolean());

        var apple = meals[2];
        foreach (var name in new[] { "portion", "portionSize", "ingredients", "photo", "photoDriveFileId", "photoPendingUpload" })
            Assert.False(apple.TryGetProperty(name, out _), name);
        Assert.Contains("meal-photos", doc.RootElement.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task UploadPending_CopiesEachPhotoToDriveOnce()
    {
        var drive = new FakeDrive();
        var settings = new InMemorySettingsStore();
        var destination = new DriveExportDestination(new FakeAuth(drive), new GoogleDriveClient(new HttpClient(drive)), settings);
        await destination.ChooseDestinationAsync(HealthExportService.FileName);
        var withPhoto = new FoodEntry { Time = Lunch, Text = "minestrone", PhotoPath = "/data/a.jpg" };
        await store.SaveFoodAsync(withPhoto);
        await store.SaveFoodAsync(new FoodEntry { Time = Lunch.AddHours(1), Text = "apple" });
        var reads = 0;
        Task<byte[]?> Read(string p) { reads++; return Task.FromResult<byte[]?>([1, 2, 3]); }
        var day = new DateOnly(2026, 11, 20);

        Assert.Null(await MealPhotos.UploadPendingAsync(store, destination, Read, day, day));
        Assert.Null(await MealPhotos.UploadPendingAsync(store, destination, Read, day, day));

        Assert.Equal(1, reads);
        var folder = drive.Items.Single(i => i.Name == FoodLog.PhotoFolderName);
        var healthPulse = drive.Items.Single(i => i.Name == DriveExportDestination.FolderName);
        Assert.Equal(healthPulse.Id, folder.Parent);
        var photo = drive.Items.Single(i => i.Parent == folder.Id);
        Assert.Equal("image/jpeg", photo.MimeType);
        Assert.StartsWith("meal-2026-11-20-1230-", photo.Name);
        var saved = (await store.GetFoodAsync(day, day)).Single(e => e.Id == withPhoto.Id);
        Assert.Equal(photo.Id, saved.PhotoDriveId);
        Assert.Equal(photo.Name, saved.PhotoName);
    }

    [Fact]
    public async Task UploadPending_ReportsAFailureAndRetriesNextTime()
    {
        await store.SaveFoodAsync(new FoodEntry { Time = Lunch, Text = "minestrone", PhotoPath = "/data/a.jpg" });
        var destination = new PhotoDestination { Fail = true };
        var day = new DateOnly(2026, 11, 20);
        Task<byte[]?> Read(string p) => Task.FromResult<byte[]?>([1]);

        var warning = await MealPhotos.UploadPendingAsync(store, destination, Read, day, day);
        Assert.Equal("Meal photos: Drive offline", warning);
        Assert.Null(Assert.Single(await store.GetFoodAsync(day, day)).PhotoDriveId);

        destination.Fail = false;
        Assert.Null(await MealPhotos.UploadPendingAsync(store, destination, Read, day, day));
        Assert.Equal("photo-1", Assert.Single(await store.GetFoodAsync(day, day)).PhotoDriveId);
    }

    [Fact]
    public async Task UploadPending_SkipsMissingFilesAndDestinationsWithoutPhotos()
    {
        await store.SaveFoodAsync(new FoodEntry { Time = Lunch, Text = "minestrone", PhotoPath = "/data/gone.jpg" });
        var day = new DateOnly(2026, 11, 20);
        var destination = new PhotoDestination();

        Assert.Null(await MealPhotos.UploadPendingAsync(store, destination, _ => Task.FromResult<byte[]?>(null), day, day));
        Assert.Equal(0, destination.Uploads);
        Assert.Null(await MealPhotos.UploadPendingAsync(store, new UnsupportedExportDestination(),
            _ => Task.FromResult<byte[]?>([1]), day, day));
    }

    [Fact]
    public async Task ExportNow_UploadsPhotosBeforeWritingTheExport()
    {
        var day = DateOnly.FromDateTime(DateTime.Today);
        await store.SaveFoodAsync(new FoodEntry { Time = DateTime.Today.AddHours(12), Text = "minestrone", PhotoPath = "/data/a.jpg" });
        var destination = new PhotoDestination();
        var exporter = new HealthExportService(new UnsupportedHealthDataService(), new LocalMigraineService(path),
            destination, new InMemorySettingsStore())
        {
            Trackers = store,
            ReadPhoto = _ => Task.FromResult<byte[]?>([1, 2]),
        };

        Assert.Null(await exporter.ExportNowAsync());

        Assert.Equal(1, destination.Uploads);
        using var doc = JsonDocument.Parse(destination.Last);
        var meal = doc.RootElement.GetProperty("days").EnumerateArray()
            .Single(d => d.GetProperty("date").GetString() == DailyCheckIn.Key(day))
            .GetProperty("meals")[0];
        Assert.StartsWith("meal-photos/meal-", meal.GetProperty("photo").GetString());
        Assert.Equal("photo-1", meal.GetProperty("photoDriveFileId").GetString());
    }

    sealed class PhotoDestination : IExportDestination
    {
        public bool Fail;
        public int Uploads;
        public string Last = "";
        public bool HasDestination => true;
        public string? DestinationName => "test";
        public Task<bool> ChooseDestinationAsync(string suggestedFileName) => Task.FromResult(true);
        public void Disconnect() { }
        public Task WriteAsync(string content)
        {
            Last = content;
            return Task.CompletedTask;
        }
        public bool CanUploadPhotos => true;
        public Task<string> UploadPhotoAsync(string name, byte[] content, string mimeType)
        {
            if (Fail) throw new IOException("Drive offline");
            return Task.FromResult($"photo-{++Uploads}");
        }
    }
}
