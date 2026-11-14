using System.Globalization;

namespace Deventry.Core.TimedMessages;

/// <summary>How a timed message's times are given (DESIGN.md §18, "Timed messages", "When").</summary>
public enum TimedScheduleKind
{
    /// <summary>A date and a time, once.</summary>
    Once,

    /// <summary>A time, on chosen days of the week.</summary>
    Days,

    /// <summary>A cron expression (<see cref="CronExpression"/>).</summary>
    Cron,
}

/// <summary>
/// When a timed message is sent (DESIGN.md §18, "Timed messages", "When"): once, on chosen days at a time, or as a cron
/// expression says, between optional dates. Times are local, in the computer's time zone as it is at the time.
/// </summary>
public sealed class TimedSchedule
{
    public TimedScheduleKind Kind { get; set; } = TimedScheduleKind.Days;

    /// <summary>The time of day, for <see cref="TimedScheduleKind.Once"/> and <see cref="TimedScheduleKind.Days"/>.</summary>
    public TimeOnly Time { get; set; } = new(7, 0);

    /// <summary>The day, for <see cref="TimedScheduleKind.Once"/>.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>The days of the week, for <see cref="TimedScheduleKind.Days"/>.</summary>
    public List<DayOfWeek> Days { get; set; } = [.. Weekdays];

    /// <summary>The expression, for <see cref="TimedScheduleKind.Cron"/>.</summary>
    public string? Cron { get; set; }

    /// <summary>None sent before this day, for <see cref="TimedScheduleKind.Days"/> and <see cref="TimedScheduleKind.Cron"/>.</summary>
    public DateOnly? From { get; set; }

    /// <summary>None sent after this day, for <see cref="TimedScheduleKind.Days"/> and <see cref="TimedScheduleKind.Cron"/>.</summary>
    public DateOnly? Until { get; set; }

    public static IReadOnlyList<DayOfWeek> Weekdays { get; } = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public static IReadOnlyList<DayOfWeek> EveryDay { get; } = [.. Weekdays, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>The cron expression read, kept while <see cref="Cron"/> stays the same.</summary>
    private (string Text, CronExpression? Expression, string? Error)? _cron;

    private (CronExpression? Expression, string? Error) ParsedCron()
    {
        var text = Cron ?? "";
        if (_cron is not { } kept || kept.Text != text)
        {
            kept = CronExpression.TryParse(text, out var expression, out var error) ? (text, expression, null) : (text, null, error);
            _cron = kept;
        }
        return (kept.Expression, kept.Error);
    }

    /// <summary>What's wrong with it, so it can't be saved; null when it can be.</summary>
    public string? Problem => Kind switch
    {
        TimedScheduleKind.Once when Date is null => "Choose the date.",
        TimedScheduleKind.Days when Days.Count == 0 => "Choose at least one day.",
        TimedScheduleKind.Cron when string.IsNullOrWhiteSpace(Cron) => "Type a cron expression.",
        TimedScheduleKind.Cron when ParsedCron().Error is { } error => error,
        not TimedScheduleKind.Once when From is { } from && Until is { } until && until < from => "Until is before From.",
        _ => null,
    };

    /// <summary>
    /// Its first time after <paramref name="after"/>, in <paramref name="zone"/>; null when there's none: a time once
    /// that has gone by, or one past <see cref="Until"/>.
    /// </summary>
    public DateTimeOffset? Next(DateTimeOffset after, TimeZoneInfo zone)
    {
        if (Problem is not null)
        {
            return null;
        }
        switch (Kind)
        {
            case TimedScheduleKind.Once:
                var once = LocalTimes.Instant(Date!.Value.ToDateTime(Time), zone);
                return once > after ? once : null;
            case TimedScheduleKind.Cron:
                return ParsedCron().Expression!.Next(after, zone, From, Until);
            default:
                var start = DateOnly.FromDateTime(LocalTimes.Local(after, zone));
                if (From is { } from && from > start)
                {
                    start = from;
                }
                // A chosen day comes within a week, or a week on when today's time has gone by.
                for (var day = start; day <= start.AddDays(7); day = day.AddDays(1))
                {
                    if (Until is { } until && day > until)
                    {
                        return null;
                    }
                    if (!Days.Contains(day.DayOfWeek))
                    {
                        continue;
                    }
                    var at = LocalTimes.Instant(day.ToDateTime(Time), zone);
                    if (at > after)
                    {
                        return at;
                    }
                }
                return null;
        }
    }

    /// <summary>
    /// When it's sent, in words: <i>Weekdays at 7:00 AM</i>, <i>Once, Nov 18 at 9:30 AM</i>, <i>0 7 * * MON-FRI</i>,
    /// with its dates.
    /// </summary>
    public string Describe(CultureInfo culture)
    {
        var time = Time.ToString("t", culture);
        var when = Kind switch
        {
            TimedScheduleKind.Once => Date is { } date ? $"Once, {Day(date, culture)} at {time}" : "Once",
            TimedScheduleKind.Cron => Cron?.Trim() is { Length: > 0 } cron ? cron : "Cron",
            _ => $"{DaysInWords(culture)} at {time}",
        };
        if (Kind == TimedScheduleKind.Once)
        {
            return when;
        }
        return (From, Until) switch
        {
            ({ } from, { } until) => $"{when}, {Day(from, culture)} to {Day(until, culture)}",
            ({ } from, null) => $"{when}, from {Day(from, culture)}",
            (null, { } until) => $"{when}, until {Day(until, culture)}",
            _ => when,
        };
    }

    private string DaysInWords(CultureInfo culture)
    {
        var days = Days.Distinct().ToHashSet();
        if (days.SetEquals(EveryDay))
        {
            return "Every day";
        }
        if (days.SetEquals(Weekdays))
        {
            return "Weekdays";
        }
        if (days.SetEquals([DayOfWeek.Saturday, DayOfWeek.Sunday]))
        {
            return "Weekends";
        }
        // In the week's order from Monday, as the editor shows them.
        return string.Join(", ", EveryDay.Where(days.Contains).Select(d => culture.DateTimeFormat.GetAbbreviatedDayName(d)));
    }

    /// <summary>A day as the culture writes a month and a day, its month short: <i>Nov 18</i>, <i>18 Nov</i>.</summary>
    public static string Day(DateOnly date, CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal);
        return date.ToString(pattern, culture);
    }

    public TimedSchedule Clone() => new()
    {
        Kind = Kind,
        Time = Time,
        Date = Date,
        Days = [.. Days],
        Cron = Cron,
        From = From,
        Until = Until,
    };
}

/// <summary>
/// Local times as instants, the way a timed message means them: a time the clock skips in spring is when the clock
/// jumps past it (2:30 at 3:00), and a time the clock shows twice in autumn is the first of them.
/// </summary>
public static class LocalTimes
{
    public static DateTimeOffset Instant(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            // The first minute after the gap, which is when the clock jumped.
            var after = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified);
            for (var i = 0; i < 24 * 60 && zone.IsInvalidTime(after); i++)
            {
                after = after.AddMinutes(1);
            }
            return new DateTimeOffset(after, zone.GetUtcOffset(after)).ToUniversalTime();
        }
        if (zone.IsAmbiguousTime(local))
        {
            // The larger offset is the earlier of the two.
            return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        }
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>The clock's time in <paramref name="zone"/> at <paramref name="instant"/>.</summary>
    public static DateTime Local(DateTimeOffset instant, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(instant, zone).DateTime;
}
