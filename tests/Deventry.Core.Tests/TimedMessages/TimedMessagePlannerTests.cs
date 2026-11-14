using Deventry.Core.TimedMessages;
using static Deventry.Core.Tests.TimedMessages.TestZones;

namespace Deventry.Core.Tests.TimedMessages;

/// <summary>DESIGN.md §18, "Timed messages": at the time, only while Deventry runs, and late.</summary>
public class TimedMessagePlannerTests
{
    private static readonly DateTimeOffset Started = EasternAt(2026, 11, 12, 18);

    private readonly Dictionary<string, DateTimeOffset> _handled = [];

    private static TimedMessage Daily(int hour, int minute = 0, string name = "Morning triage") => new()
    {
        Name = name,
        Folder = "/work/starfall",
        Text = "Triage the overnight issues.",
        Schedule = new TimedSchedule { Kind = TimedScheduleKind.Days, Time = new TimeOnly(hour, minute), Days = [.. TimedSchedule.EveryDay] },
        SavedAt = EasternAt(2026, 11, 1, 12),
    };

    private TimedMessagePlan Plan(DateTimeOffset now, DateTimeOffset? started = null, params TimedMessage[] messages) =>
        TimedMessagePlanner.Plan(messages, _handled, now, started ?? Started, Eastern);

    [Fact]
    public void A_message_is_due_at_its_time()
    {
        var message = Daily(7);
        Assert.Empty(Plan(EasternAt(2026, 11, 13, 6, 59), messages: message).Due);

        var plan = Plan(EasternAt(2026, 11, 13, 7), messages: message);

        var due = Assert.Single(plan.Due);
        Assert.Same(message, due.Message);
        Assert.Equal(EasternAt(2026, 11, 13, 7), due.At);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Once_handled_it_isnt_due_again_and_the_next_is_tomorrow()
    {
        var message = Daily(7);
        _handled[message.Id] = EasternAt(2026, 11, 13, 7);

        var plan = Plan(EasternAt(2026, 11, 13, 7, 5), messages: message);

        Assert.Empty(plan.Due);
        Assert.Equal(EasternAt(2026, 11, 14, 7), plan.Next?.At);
    }

    [Fact]
    public void Up_to_half_an_hour_late_its_still_sent_and_later_its_skipped()
    {
        var message = Daily(7);
        Assert.Single(Plan(EasternAt(2026, 11, 13, 7, 30), messages: message).Due);

        var late = Plan(EasternAt(2026, 11, 13, 7, 31), messages: message);

        Assert.Empty(late.Due);
        var skipped = Assert.Single(late.Skipped);
        Assert.Equal(EasternAt(2026, 11, 13, 7), skipped.At);
    }

    [Fact]
    public void A_time_that_came_before_Deventry_started_isnt_sent()
    {
        var message = Daily(7);

        var plan = Plan(EasternAt(2026, 11, 13, 7, 5), EasternAt(2026, 11, 13, 7, 1), message);

        Assert.Empty(plan.Due);
        Assert.Empty(plan.Skipped);
        Assert.Equal(EasternAt(2026, 11, 14, 7), plan.Next?.At);
    }

    [Fact]
    public void A_message_saved_after_its_time_waits_for_the_next()
    {
        var message = Daily(7);
        message.SavedAt = EasternAt(2026, 11, 13, 7, 2);

        var plan = Plan(EasternAt(2026, 11, 13, 7, 5), messages: message);

        Assert.Empty(plan.Due);
        Assert.Equal(EasternAt(2026, 11, 14, 7), plan.Next?.At);
    }

    [Fact]
    public void Of_several_times_that_went_by_in_sleep_only_the_latest_counts()
    {
        var every15 = Daily(0, name: "Check the build");
        every15.Schedule = new TimedSchedule { Kind = TimedScheduleKind.Cron, Cron = "*/15 * * * *" };
        _handled[every15.Id] = EasternAt(2026, 11, 13, 6);

        var plan = Plan(EasternAt(2026, 11, 13, 7, 20), messages: every15);

        var due = Assert.Single(plan.Due);
        Assert.Equal(EasternAt(2026, 11, 13, 7, 15), due.At);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void A_message_thats_off_or_cant_be_sent_is_left_alone()
    {
        var off = Daily(7);
        off.IsOn = false;
        var broken = Daily(7);
        broken.Schedule = new TimedSchedule { Kind = TimedScheduleKind.Cron, Cron = "nonsense" };

        var plan = Plan(EasternAt(2026, 11, 13, 7), messages: [off, broken]);

        Assert.Empty(plan.Due);
        Assert.Null(plan.Next);
    }

    [Fact]
    public void The_next_is_the_earliest_and_the_wake_a_minute_before()
    {
        var seven = Daily(7);
        var six = Daily(6, 30, "Build check");
        var now = EasternAt(2026, 11, 13, 1);

        var plan = Plan(now, messages: [seven, six]);

        Assert.Same(six, plan.Next?.Message);
        Assert.Equal(EasternAt(2026, 11, 13, 6, 29), plan.WakeAt(now));
        Assert.False(plan.IsDueSoon(now));
        Assert.True(plan.IsDueSoon(EasternAt(2026, 11, 13, 6, 28)));
        // Within the minute before, there's nothing left to wake for.
        Assert.Null(plan.WakeAt(EasternAt(2026, 11, 13, 6, 29).AddSeconds(30)));
    }

    [Fact]
    public void A_message_once_isnt_next_after_its_time()
    {
        var once = Daily(9, 30);
        once.Schedule = new TimedSchedule { Kind = TimedScheduleKind.Once, Date = new DateOnly(2026, 11, 13), Time = new TimeOnly(9, 30) };
        _handled[once.Id] = EasternAt(2026, 11, 13, 9, 30);

        Assert.Null(TimedMessagePlanner.NextFor(once, _handled, EasternAt(2026, 11, 13, 9, 31), Started, Eastern));
        Assert.Null(Plan(EasternAt(2026, 11, 13, 9, 31), messages: once).Next);
    }
}
