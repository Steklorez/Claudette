using System.Globalization;

namespace Claudette.Core;

/// <summary>How Claudette writes times, durations, sizes and names, the same wherever they show.</summary>
public static class Formats
{
    /// <summary>How long ago: "just now", "5 min ago", "2 h ago", "yesterday", "3 days ago".</summary>
    public static string Ago(TimeSpan span) => span switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } => string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes} min ago"),
        { TotalDays: < 1 } => string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours} h ago"),
        { TotalDays: < 2 } => "yesterday",
        _ => string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays} days ago"),
    };

    /// <summary>
    /// A running time that ticks each second, such as the working line's or a job's: "8s", "1m 05s", "1h 02m". Seconds
    /// show until an hour has gone.
    /// </summary>
    public static string Elapsed(TimeSpan span) => span switch
    {
        { TotalHours: >= 1 } => string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes:00}m"),
        { TotalMinutes: >= 1 } => string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}m {span.Seconds:00}s"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, (int)span.TotalSeconds)}s"),
    };

    /// <summary>How long something has run or took, in round figures: "8s", "3m", "1h 02m", "2d 3h".</summary>
    public static string Duration(TimeSpan span) => span switch
    {
        { TotalMinutes: < 1 } => string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, (int)span.TotalSeconds)}s"),
        { TotalHours: < 1 } => string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}m"),
        { TotalDays: < 1 } => string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes:00}m"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays}d {span.Hours}h"),
    };

    /// <summary>"at 15:45" today, else "on Mon at 09:00", in the clock's time zone.</summary>
    public static string When(DateTimeOffset time, TimeProvider clock)
    {
        var local = TimeZoneInfo.ConvertTime(time, clock.LocalTimeZone);
        var today = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone).Date;
        return local.Date == today
            ? $"at {local.ToString("t", CultureInfo.CurrentCulture)}"
            : $"on {local.ToString("ddd", CultureInfo.CurrentCulture)} at {local.ToString("t", CultureInfo.CurrentCulture)}";
    }

    /// <summary>A folder's own name, with or without a separator at its end; the whole path when it has none (a drive's root).</summary>
    public static string FolderName(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : path;

    /// <summary>An amount in US dollars, as the token counts' costs show: "$0.75", "$12.40".</summary>
    public static string Dollars(double usd) => string.Create(CultureInfo.InvariantCulture, $"${usd:0.00}");

    /// <summary>A size on disk: "3.2 GB", "140 MB", "12 KB", "512 bytes".</summary>
    public static string Size(long bytes)
    {
        const double kb = 1024, mb = kb * 1024, gb = mb * 1024;
        // A figure that would round to 1024 moves up a unit: "1.0 GB", not "1024 MB".
        return bytes switch
        {
            >= (long)(gb - mb / 2) => (bytes / gb).ToString("0.0", CultureInfo.InvariantCulture) + " GB",
            >= (long)(mb - kb / 2) => Math.Round(bytes / mb).ToString("0", CultureInfo.InvariantCulture) + " MB",
            >= (long)kb => Math.Round(bytes / kb).ToString("0", CultureInfo.InvariantCulture) + " KB",
            _ => bytes.ToString(CultureInfo.InvariantCulture) + " bytes",
        };
    }

    /// <summary>"High" for "high".</summary>
    public static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>"a", "a and b", "a, b and c": names in a sentence.</summary>
    public static string List(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };
}
