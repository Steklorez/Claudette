using System.Globalization;
using Deventry.Core.TimedMessages;
using static Deventry.Core.Tests.TimedMessages.TestZones;

namespace Deventry.Core.Tests.TimedMessages;

/// <summary>DESIGN.md §18, "Timed messages", "When".</summary>
public class TimedScheduleTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static TimedSchedule OnDays(int hour, int minute, params DayOfWeek[] days) =>
        new() { Kind = TimedScheduleKind.Days, Time = new TimeOnly(hour, minute), Days = [.. days] };

    [Fact]
    public void Weekdays_at_seven_come_next_on_the_next_weekday()
    {
        var schedule = OnDays(7, 0, [.. TimedSchedule.Weekdays]);
        // Friday 13 Nov 2026, 8:00: today's has gone by, so Monday's.
        Assert.Equal(EasternAt(2026, 11, 16, 7), schedule.Next(EasternAt(2026, 11, 13, 8), Eastern));
        // Friday at 6:59: today's.
        Assert.Equal(EasternAt(2026, 11, 13, 7), schedule.Next(EasternAt(2026, 11, 13, 6, 59), Eastern));
    }

    [Fact]
    public void A_time_is_strictly_after_the_one_given()
    {
        var schedule = OnDays(7, 0, [.. TimedSchedule.EveryDay]);
        var seven = EasternAt(2026, 11, 13, 7);
        Assert.Equal(EasternAt(2026, 11, 14, 7), schedule.Next(seven, Eastern));
    }

    [Fact]
    public void Once_is_its_date_and_time_until_that_has_gone_by()
    {
        var schedule = new TimedSchedule { Kind = TimedScheduleKind.Once, Date = new DateOnly(2026, 11, 18), Time = new TimeOnly(9, 30) };
        Assert.Equal(EasternAt(2026, 11, 18, 9, 30), schedule.Next(EasternAt(2026, 11, 1, 0), Eastern));
        Assert.Null(schedule.Next(EasternAt(2026, 11, 18, 9, 30), Eastern));
    }

    [Fact]
    public void From_and_until_bound_the_days()
    {
        var schedule = OnDays(7, 0, [.. TimedSchedule.EveryDay]);
        schedule.From = new DateOnly(2026, 12, 1);
        schedule.Until = new DateOnly(2026, 12, 2);
        Assert.Equal(EasternAt(2026, 12, 1, 7), schedule.Next(EasternAt(2026, 11, 13, 7), Eastern));
        Assert.Equal(EasternAt(2026, 12, 2, 7), schedule.Next(EasternAt(2026, 12, 1, 7), Eastern));
        Assert.Null(schedule.Next(EasternAt(2026, 12, 2, 7), Eastern));
    }

    [Fact]
    public void A_time_the_clock_skips_in_spring_is_sent_when_the_clock_jumps_past_it()
    {
        // Sunday 8 March 2026: 2:00 becomes 3:00.
        var schedule = OnDays(2, 30, [.. TimedSchedule.EveryDay]);
        var next = schedule.Next(EasternAt(2026, 3, 7, 12), Eastern);
        Assert.Equal(EasternAt(2026, 3, 8, 3), next);
        // And the next day, as usual.
        Assert.Equal(EasternAt(2026, 3, 9, 2, 30), schedule.Next(next!.Value, Eastern));
    }

    [Fact]
    public void A_time_the_clock_shows_twice_in_autumn_is_sent_once_the_first()
    {
        // Sunday 1 November 2026: 2:00 goes back to 1:00, so 1:30 happens twice, at UTC−4 then UTC−5.
        var schedule = OnDays(1, 30, [.. TimedSchedule.EveryDay]);
        var first = new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-4));
        Assert.Equal(first, schedule.Next(EasternAt(2026, 10, 31, 12), Eastern));
        // Not again an hour later: the next is Monday's.
        Assert.Equal(EasternAt(2026, 11, 2, 1, 30), schedule.Next(first, Eastern));
    }

    [Fact]
    public void A_cron_schedule_uses_its_expression_within_its_dates()
    {
        var schedule = new TimedSchedule { Kind = TimedScheduleKind.Cron, Cron = "0 9,14 * * MON-FRI", Until = new DateOnly(2026, 11, 16) };
        var friday = EasternAt(2026, 11, 13, 10);
        Assert.Equal(EasternAt(2026, 11, 13, 14), schedule.Next(friday, Eastern));
        Assert.Equal(EasternAt(2026, 11, 16, 9), schedule.Next(EasternAt(2026, 11, 13, 14), Eastern));
        Assert.Null(schedule.Next(EasternAt(2026, 11, 16, 14), Eastern));
    }

    [Theory]
    [InlineData(TimedScheduleKind.Once, null, "Choose the date.")]
    [InlineData(TimedScheduleKind.Days, null, "Choose at least one day.")]
    [InlineData(TimedScheduleKind.Cron, "", "Type a cron expression.")]
    [InlineData(TimedScheduleKind.Cron, "0 25 * * *", "Hour 25 is past 23.")]
    public void One_that_cant_be_sent_says_why(TimedScheduleKind kind, string? cron, string problem)
    {
        var schedule = new TimedSchedule { Kind = kind, Cron = cron, Days = [] };
        Assert.Equal(problem, schedule.Problem);
        Assert.Null(schedule.Next(EasternAt(2026, 11, 13, 8), Eastern));
    }

    [Fact]
    public void Until_before_from_is_a_problem()
    {
        var schedule = OnDays(7, 0, DayOfWeek.Monday);
        schedule.From = new DateOnly(2026, 12, 2);
        schedule.Until = new DateOnly(2026, 12, 1);
        Assert.Equal("Until is before From.", schedule.Problem);
    }

    [Fact]
    public void It_says_when_in_words()
    {
        // The times as the culture writes them: some systems put a narrow space before AM.
        static string At(int hour, int minute) => new TimeOnly(hour, minute).ToString("t", English);
        Assert.Equal($"Weekdays at {At(7, 0)}", OnDays(7, 0, [.. TimedSchedule.Weekdays]).Describe(English));
        Assert.Equal($"Every day at {At(18, 15)}", OnDays(18, 15, [.. TimedSchedule.EveryDay]).Describe(English));
        Assert.Equal($"Weekends at {At(9, 0)}", OnDays(9, 0, DayOfWeek.Sunday, DayOfWeek.Saturday).Describe(English));
        Assert.Equal($"Mon, Wed, Sun at {At(7, 0)}", OnDays(7, 0, DayOfWeek.Sunday, DayOfWeek.Wednesday, DayOfWeek.Monday).Describe(English));
        Assert.Equal($"Once, Nov 18 at {At(9, 30)}",
            new TimedSchedule { Kind = TimedScheduleKind.Once, Date = new DateOnly(2026, 11, 18), Time = new TimeOnly(9, 30) }.Describe(English));
        Assert.Equal("0 7 * * MON-FRI, until Dec 1",
            new TimedSchedule { Kind = TimedScheduleKind.Cron, Cron = " 0 7 * * MON-FRI ", Until = new DateOnly(2026, 12, 1) }.Describe(English));
        var dated = OnDays(7, 0, [.. TimedSchedule.Weekdays]);
        dated.From = new DateOnly(2026, 12, 1);
        dated.Until = new DateOnly(2026, 12, 19);
        Assert.Equal($"Weekdays at {At(7, 0)}, Dec 1 to Dec 19", dated.Describe(English));
    }

    [Fact]
    public void A_worktree_takes_the_messages_name()
    {
        Assert.Equal("morning-triage", new TimedMessage { Name = "Morning triage" }.WorktreeName);
        Assert.Equal("check-the-nightly-build", new TimedMessage { Name = "  Check the nightly build!! " }.WorktreeName);
        Assert.Equal("timed-message", new TimedMessage { Name = "✨" }.WorktreeName);
    }
}
