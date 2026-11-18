using System.Xml.Linq;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public class ClockNowTests
{
    [Fact]
    public void TimeIsTheCurrentMinuteWithoutSeconds()
    {
        var now = new DateTime(2026, 11, 18, 18, 32, 47, 512);
        Assert.Equal(new TimeSpan(18, 32, 0), ClockNow.Time(now));
    }

    [Fact]
    public void StampSetsTodayAndTheCurrentMinute()
    {
        var now = new DateTime(2026, 11, 18, 0, 5, 59);
        var (date, clock) = ClockNow.Stamp(now);
        Assert.Equal(new DateTime(2026, 11, 18), date);
        Assert.Equal(TimeSpan.Zero, date.TimeOfDay);
        Assert.Equal(new TimeSpan(0, 5, 0), clock);
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "HealthPulse.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }

    [Fact]
    public void EveryTimePickerHasANowButtonNextToIt()
    {
        var views = Directory.GetFiles(Path.Combine(RepoRoot(), "Views"), "*.xaml");
        var pickers = 0;
        foreach (var file in views)
        {
            var doc = XDocument.Load(file);
            foreach (var picker in doc.Descendants().Where(e => e.Name.LocalName == "TimePicker"))
            {
                pickers++;
                var hasNow = picker.Parent!.Elements().Any(e =>
                    e.Name.LocalName == "Button" &&
                    ((string?)e.Attribute("Text") ?? "").Contains("now", StringComparison.OrdinalIgnoreCase));
                Assert.True(hasNow, $"{Path.GetFileName(file)}: TimePicker bound to {(string?)picker.Attribute("Time")} has no Now button beside it");
            }
        }
        Assert.True(pickers >= 7, $"Expected at least 7 time pickers, found {pickers}");
    }
}
