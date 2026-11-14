using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Deventry.Core.TimedMessages;

/// <summary>
/// A five-field cron expression for a timed message's <b>Advanced</b> times (DESIGN.md §18, "Timed messages", "When"):
/// minute, hour, day of the month, month and day of the week, each a list of <c>*</c>, numbers, ranges and steps
/// (<c>*/15</c>, <c>1-5</c>, <c>9,14</c>, <c>8-18/2</c>), with the months' and days' names (<c>JAN</c>, <c>MON-FRI</c>).
/// Sunday is 0 or 7. As cron has it, when both day fields are given (neither starts with <c>*</c>), a day that either one
/// matches counts; otherwise both must. Times are the computer's local ones (<see cref="LocalTimes"/>).
/// </summary>
public sealed class CronExpression
{
    private static readonly string[] MonthNames = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
    private static readonly string[] DayNames = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    private readonly ulong _minutes;
    private readonly ulong _hours;
    private readonly ulong _daysOfMonth;
    private readonly ulong _months;
    private readonly ulong _daysOfWeek;
    private readonly bool _anyDayOfMonth;
    private readonly bool _anyDayOfWeek;

    private CronExpression(string text, ulong[] fields, bool anyDayOfMonth, bool anyDayOfWeek)
    {
        Text = text;
        (_minutes, _hours, _daysOfMonth, _months, _daysOfWeek) = (fields[0], fields[1], fields[2], fields[3], fields[4]);
        _anyDayOfMonth = anyDayOfMonth;
        _anyDayOfWeek = anyDayOfWeek;
    }

    /// <summary>The expression as it was given, trimmed.</summary>
    public string Text { get; }

    private sealed record Field(string Name, int Min, int Max, string[]? Names, int NameBase);

    private static readonly Field[] Fields =
    [
        new("Minute", 0, 59, null, 0),
        new("Hour", 0, 23, null, 0),
        new("Day of the month", 1, 31, null, 0),
        new("Month", 1, 12, MonthNames, 1),
        new("Day of the week", 0, 7, DayNames, 0),
    ];

    public static CronExpression Parse(string text) =>
        TryParse(text, out var expression, out var error) ? expression : throw new FormatException(error);

    /// <summary>Reads <paramref name="text"/>; when it can't, <paramref name="error"/> says where and why, for the editor.</summary>
    public static bool TryParse(string text, [NotNullWhen(true)] out CronExpression? expression, [NotNullWhen(false)] out string? error)
    {
        expression = null;
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != Fields.Length)
        {
            error = $"A cron expression has 5 fields: minute, hour, day of the month, month and day of the week. This has {parts.Length}.";
            return false;
        }
        var bits = new ulong[Fields.Length];
        for (var i = 0; i < Fields.Length; i++)
        {
            if (!TryField(parts[i], Fields[i], out bits[i], out error))
            {
                return false;
            }
        }
        // Sunday is 0 or 7.
        if ((bits[4] & (1UL << 7)) != 0)
        {
            bits[4] = (bits[4] & ~(1UL << 7)) | 1UL;
        }
        expression = new CronExpression(string.Join(' ', parts), bits, parts[2].StartsWith('*'), parts[4].StartsWith('*'));
        error = null;
        return true;
    }

    private static bool TryField(string text, Field field, out ulong bits, [NotNullWhen(false)] out string? error)
    {
        bits = 0;
        foreach (var item in text.Split(','))
        {
            if (item.Length == 0)
            {
                error = $"{field.Name} has an empty item in \"{text}\".";
                return false;
            }
            var slash = item.Split('/');
            if (slash.Length > 2)
            {
                error = $"{field.Name} has more than one / in \"{item}\".";
                return false;
            }
            var step = 1;
            if (slash.Length == 2 && (!int.TryParse(slash[1], NumberStyles.None, CultureInfo.InvariantCulture, out step) || step == 0))
            {
                error = $"{field.Name}'s step \"{slash[1]}\" isn't a whole number above 0.";
                return false;
            }
            int low, high;
            if (slash[0] == "*")
            {
                (low, high) = (field.Min, field.Max);
            }
            else if (slash[0].Split('-') is [var from, var to])
            {
                if (!TryValue(from, field, out low, out error) || !TryValue(to, field, out high, out error))
                {
                    return false;
                }
                if (low > high)
                {
                    error = $"{field.Name} range {slash[0]} runs backwards.";
                    return false;
                }
            }
            else if (TryValue(slash[0], field, out low, out error))
            {
                // A value with a step runs to the end: 5/15 is 5, 20, 35 and 50.
                high = slash.Length == 2 ? field.Max : low;
            }
            else
            {
                return false;
            }
            for (var value = low; value <= high; value += step)
            {
                bits |= 1UL << value;
            }
        }
        error = null;
        return true;
    }

    private static bool TryValue(string text, Field field, out int value, [NotNullWhen(false)] out string? error)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            if (value < field.Min)
            {
                error = $"{field.Name} {value} is before {field.Min}.";
                return false;
            }
            if (value > field.Max)
            {
                error = $"{field.Name} {value} is past {field.Max}.";
                return false;
            }
            error = null;
            return true;
        }
        if (field.Names is { } names && Array.FindIndex(names, n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase)) is >= 0 and var index)
        {
            value = index + field.NameBase;
            error = null;
            return true;
        }
        error = field.Names is null
            ? $"{field.Name} \"{text}\" isn't a number."
            : $"{field.Name} \"{text}\" isn't a number or a name such as {field.Names[0]}.";
        return false;
    }

    /// <summary>Whether <paramref name="day"/> is one of its days.</summary>
    public bool IsDay(DateOnly day)
    {
        if ((_months & (1UL << day.Month)) == 0)
        {
            return false;
        }
        var ofMonth = (_daysOfMonth & (1UL << day.Day)) != 0;
        var ofWeek = (_daysOfWeek & (1UL << (int)day.DayOfWeek)) != 0;
        // One field left as * leaves the other to decide; both given, either will do.
        return _anyDayOfMonth || _anyDayOfWeek ? ofMonth && ofWeek : ofMonth || ofWeek;
    }

    /// <summary>
    /// Its first time after <paramref name="after"/>, in <paramref name="zone"/>, on or after <paramref name="from"/> and
    /// not after <paramref name="until"/>; null when there's none within five years (February 30th, say).
    /// </summary>
    public DateTimeOffset? Next(DateTimeOffset after, TimeZoneInfo zone, DateOnly? from = null, DateOnly? until = null)
    {
        var start = DateOnly.FromDateTime(LocalTimes.Local(after, zone));
        if (from is { } first && first > start)
        {
            start = first;
        }
        for (var i = 0; i < 366 * 5; i++)
        {
            var day = start.AddDays(i);
            if (until is { } last && day > last)
            {
                return null;
            }
            if (!IsDay(day))
            {
                continue;
            }
            for (var hour = 0; hour < 24; hour++)
            {
                if ((_hours & (1UL << hour)) == 0)
                {
                    continue;
                }
                for (var minute = 0; minute < 60; minute++)
                {
                    if ((_minutes & (1UL << minute)) == 0)
                    {
                        continue;
                    }
                    var at = LocalTimes.Instant(day.ToDateTime(new TimeOnly(hour, minute)), zone);
                    if (at > after)
                    {
                        return at;
                    }
                }
            }
        }
        return null;
    }

    public override string ToString() => Text;
}
