using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public class MigraineFormTests
{
    static readonly DateTime Now = new(2026, 11, 17, 12, 34, 56);

    [Fact]
    public void NewEntry_StartsOngoingWithAllStandardTriggersUnselected()
    {
        var form = MigraineForm.FromEntry(new MigraineEntry { StartTime = Now }, Now);

        Assert.True(form.IsOngoing);
        Assert.Equal(new TimeSpan(12, 34, 0), form.StartClock);
        Assert.Equal(MigraineEntry.TriggerOptions, form.Triggers.Select(t => t.Name));
        Assert.DoesNotContain(form.Triggers, t => t.Selected);
        Assert.Equal(0, form.ReliefIndex);
    }

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var original = new MigraineEntry
        {
            StartTime = new DateTime(2026, 11, 16, 9, 15, 0),
            EndTime = new DateTime(2026, 11, 16, 17, 45, 0),
            PeakSeverity = 8,
            Aura = true,
            Nausea = true,
            LightSensitivity = true,
            SoundSensitivity = false,
            TriptanTaken = true,
            TriptanTime = new DateTime(2026, 11, 16, 9, 40, 0),
            TriptanRelief = "Partial",
            OtherMeds = "Ibuprofen 400 mg",
            Notes = "Skipped lunch"
        };
        original.TriggerList = ["Skipped meal", "Bright lights"];

        var form = MigraineForm.FromEntry(original, Now);
        var copy = new MigraineEntry { Id = original.Id };
        form.ApplyTo(copy);

        Assert.Equal(original.StartTime, copy.StartTime);
        Assert.Equal(original.EndTime, copy.EndTime);
        Assert.Equal(8, copy.PeakSeverity);
        Assert.True(copy.Aura && copy.Nausea && copy.LightSensitivity);
        Assert.False(copy.SoundSensitivity);
        Assert.Equal(original.TriptanTime, copy.TriptanTime);
        Assert.Equal("Partial", copy.TriptanRelief);
        Assert.Equal("Ibuprofen 400 mg", copy.OtherMeds);
        Assert.Equal("Skipped lunch", copy.Notes);
        Assert.Equal(["Skipped meal", "Bright lights"], copy.TriggerList);
        Assert.Contains(form.Triggers, t => t.Name == "Bright lights" && t.Selected);
    }

    [Fact]
    public void Apply_ClearsTriptanDetailsWhenNotTaken_AndBlankTextBecomesNull()
    {
        var form = MigraineForm.FromEntry(new MigraineEntry { StartTime = Now }, Now);
        form.TriptanTaken = false;
        form.ReliefIndex = 1;
        form.OtherMeds = "   ";
        form.Notes = "  tired  ";
        form.Severity = 6.6;

        var entry = new MigraineEntry();
        form.ApplyTo(entry);

        Assert.Null(entry.TriptanTime);
        Assert.Null(entry.TriptanRelief);
        Assert.Null(entry.OtherMeds);
        Assert.Equal("tired", entry.Notes);
        Assert.Equal(7, entry.PeakSeverity);
        Assert.Null(entry.EndTime);
    }

    [Fact]
    public void Apply_UsesEndTimeOnlyWhenNotOngoing()
    {
        var form = MigraineForm.FromEntry(new MigraineEntry { StartTime = Now }, Now);
        form.IsOngoing = false;
        form.EndDate = Now.Date;
        form.EndClock = new TimeSpan(18, 0, 0);
        var entry = new MigraineEntry();
        form.ApplyTo(entry);
        Assert.Equal(Now.Date.AddHours(18), entry.EndTime);
    }

    [Theory]
    [InlineData(1, "Full")]
    [InlineData(2, "Partial")]
    [InlineData(3, "None")]
    [InlineData(0, null)]
    public void ReliefIndex_MapsToReliefOption(int index, string? expected)
    {
        var form = MigraineForm.FromEntry(new MigraineEntry { StartTime = Now }, Now);
        form.TriptanTaken = true;
        form.ReliefIndex = index;
        var entry = new MigraineEntry();
        form.ApplyTo(entry);
        Assert.Equal(expected, entry.TriptanRelief);
        Assert.Equal(MigraineForm.ReliefChoices[index], expected ?? MigraineForm.ReliefUnknown);
    }
}
