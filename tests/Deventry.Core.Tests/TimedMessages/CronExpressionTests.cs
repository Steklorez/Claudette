using Deventry.Core.TimedMessages;
using static Deventry.Core.Tests.TimedMessages.TestZones;

namespace Deventry.Core.Tests.TimedMessages;

/// <summary>DESIGN.md §18, "Timed messages", "When": the Advanced cron expression.</summary>
public class CronExpressionTests
{
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) => new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("0 7 * * MON-FRI")]
    [InlineData("*/15 * * * *")]
    [InlineData("30 9,14 1 * *")]
    [InlineData("0 8-18/2 * jan-mar sun")]
    [InlineData("5/20 0 L * *", false)]
    [InlineData("0 0 * * 7")]
    public void It_reads_the_usual_forms(string text, bool reads = true)
    {
        Assert.Equal(reads, CronExpression.TryParse(text, out _, out _));
    }

    [Theory]
    [InlineData("0 7 * *", "A cron expression has 5 fields: minute, hour, day of the month, month and day of the week. This has 4.")]
    [InlineData("0 25 * * *", "Hour 25 is past 23.")]
    [InlineData("60 * * * *", "Minute 60 is past 59.")]
    [InlineData("0 0 0 * *", "Day of the month 0 is before 1.")]
    [InlineData("0 0 * 13 *", "Month 13 is past 12.")]
    [InlineData("0 0 * * 8", "Day of the week 8 is past 7.")]
    [InlineData("0 0 * FOO *", "Month \"FOO\" isn't a number or a name such as JAN.")]
    [InlineData("x * * * *", "Minute \"x\" isn't a number.")]
    [InlineData("0 5-3 * * *", "Hour range 5-3 runs backwards.")]
    [InlineData("*/0 * * * *", "Minute's step \"0\" isn't a whole number above 0.")]
    [InlineData("1,,2 * * * *", "Minute has an empty item in \"1,,2\".")]
    public void One_that_doesnt_read_says_where_and_why(string text, string error)
    {
        Assert.False(CronExpression.TryParse(text, out _, out var said));
        Assert.Equal(error, said);
    }

    [Fact]
    public void Steps_ranges_and_lists_give_their_times()
    {
        var quarter = CronExpression.Parse("*/15 * * * *");
        Assert.Equal(Utc(2026, 11, 13, 10, 15), quarter.Next(Utc(2026, 11, 13, 10, 0), TestZones.Utc));
        Assert.Equal(Utc(2026, 11, 13, 11, 0), quarter.Next(Utc(2026, 11, 13, 10, 45), TestZones.Utc));

        var fromFive = CronExpression.Parse("5/20 * * * *");
        Assert.Equal(Utc(2026, 11, 13, 10, 25), fromFive.Next(Utc(2026, 11, 13, 10, 5), TestZones.Utc));

        var twice = CronExpression.Parse("30 9,14 * * *");
        Assert.Equal(Utc(2026, 11, 13, 14, 30), twice.Next(Utc(2026, 11, 13, 9, 30), TestZones.Utc));
    }

    [Fact]
    public void Sunday_is_0_or_7()
    {
        Assert.True(CronExpression.Parse("0 0 * * 7").IsDay(new DateOnly(2026, 11, 15)));
        Assert.True(CronExpression.Parse("0 0 * * SUN").IsDay(new DateOnly(2026, 11, 15)));
        Assert.True(CronExpression.Parse("0 0 * * 5-7").IsDay(new DateOnly(2026, 11, 15)));
        Assert.False(CronExpression.Parse("0 0 * * 1-5").IsDay(new DateOnly(2026, 11, 15)));
    }

    [Fact]
    public void Both_day_fields_given_means_either_one_as_cron_has_it()
    {
        // The 1st of the month, or any Monday.
        var either = CronExpression.Parse("0 9 1 * MON");
        Assert.True(either.IsDay(new DateOnly(2026, 12, 1)));
        Assert.True(either.IsDay(new DateOnly(2026, 11, 16)));
        Assert.False(either.IsDay(new DateOnly(2026, 11, 17)));
        // One left as * leaves the other to decide.
        var mondays = CronExpression.Parse("0 9 * * MON");
        Assert.False(mondays.IsDay(new DateOnly(2026, 12, 1)));
        var everyOtherDay = CronExpression.Parse("0 9 */2 * MON");
        Assert.False(everyOtherDay.IsDay(new DateOnly(2026, 11, 17)));
    }

    [Fact]
    public void A_leap_day_waits_for_its_year_and_an_impossible_one_never_comes()
    {
        Assert.Equal(Utc(2028, 2, 29, 0), CronExpression.Parse("0 0 29 2 *").Next(Utc(2026, 11, 13, 0), TestZones.Utc));
        Assert.Null(CronExpression.Parse("0 0 30 2 *").Next(Utc(2026, 11, 13, 0), TestZones.Utc));
    }

    [Fact]
    public void Its_times_are_local()
    {
        // 7:00 in Eastern daylight time is 11:00 UTC; in standard time, 12:00.
        var seven = CronExpression.Parse("0 7 * * *");
        Assert.Equal(Utc(2026, 10, 30, 11), seven.Next(Utc(2026, 10, 30, 0), Eastern));
        Assert.Equal(Utc(2026, 11, 13, 12), seven.Next(Utc(2026, 11, 13, 0), Eastern));
    }

    [Fact]
    public void Its_text_is_kept_tidy()
    {
        Assert.Equal("0 7 * * MON-FRI", CronExpression.Parse("  0   7 *  * MON-FRI ").Text);
    }
}
