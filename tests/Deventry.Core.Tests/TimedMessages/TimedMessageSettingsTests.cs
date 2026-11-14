using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Settings;
using Deventry.Core.TimedMessages;

namespace Deventry.Core.Tests.TimedMessages;

/// <summary>DESIGN.md §18, "Timed messages": kept in <c>settings.json</c>, on this computer only.</summary>
public class TimedMessageSettingsTests
{
    [Fact]
    public void A_timed_message_reads_back_as_it_was_written()
    {
        var settings = new AppSettings();
        settings.TimedMessages.Messages.Add(new TimedMessage
        {
            Name = "Morning triage",
            Folder = "/work/starfall",
            Text = "Triage the overnight issues.",
            InWorktree = true,
            PermissionMode = "plan",
            Schedule = new TimedSchedule
            {
                Kind = TimedScheduleKind.Days,
                Time = new TimeOnly(7, 0),
                Days = [DayOfWeek.Monday, DayOfWeek.Friday],
                From = new DateOnly(2026, 12, 1),
            },
            SavedAt = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero),
        });

        var json = JsonFileStore<AppSettings>.Serialize(settings);
        var read = JsonSerializer.Deserialize<AppSettings>(json, JsonFileStore<AppSettings>.Options)!.TimedMessages.Messages.Single();

        var schedule = JsonNode.Parse(json)!["timedMessages"]!["messages"]![0]!["schedule"]!;
        Assert.Equal("days", schedule["kind"]!.GetValue<string>());
        Assert.Equal("07:00:00", schedule["time"]!.GetValue<string>());
        Assert.Equal("monday", schedule["days"]![0]!.GetValue<string>());
        Assert.Equal("2026-12-01", schedule["from"]!.GetValue<string>());
        Assert.Equal("Morning triage", read.Name);
        Assert.True(read.InWorktree);
        Assert.Equal("plan", read.PermissionMode);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], read.Schedule.Days);
        Assert.Equal(new DateOnly(2026, 12, 1), read.Schedule.From);
        Assert.Equal(new TimeOnly(7, 0), read.Schedule.Time);
    }

    [Fact]
    public void Timed_messages_are_this_computers()
    {
        Assert.False(SyncedSettings.Syncs("timedMessages.messages"));
    }

    [Fact]
    public void A_copy_keeps_everything_but_is_its_own()
    {
        var message = new TimedMessage { Name = "Build check", Schedule = new TimedSchedule { Kind = TimedScheduleKind.Cron, Cron = "0 6 * * *" } };

        var copy = message.Clone();
        copy.Schedule.Days.Clear();

        Assert.Equal(message.Id, copy.Id);
        Assert.Equal("0 6 * * *", copy.Schedule.Cron);
        Assert.NotEmpty(message.Schedule.Days);
    }
}
