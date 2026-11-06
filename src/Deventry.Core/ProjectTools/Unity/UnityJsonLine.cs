using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.ProjectTools.EditorLogs;

namespace Deventry.Core.ProjectTools.Unity;

/// <summary>
/// A line of the JSON Lines log Unity 6's logging framework writes beside its text log, <c>Editor.jsonl</c>, with its JSON
/// logging on (DESIGN.md §18, "Editor logs", "Unity's JSON log"): one entry, as an object. Unity doesn't document its
/// fields yet, so they're read tolerantly, by the names such logs use, ignoring case: the time (<c>timestamp</c>,
/// <c>time</c>), the level (<c>level</c>, <c>severity</c>, <c>logType</c>), the message (<c>message</c>, <c>msg</c>), the
/// category (<c>category</c>, <c>channel</c>, <c>tag</c>) and the stack trace (<c>stackTrace</c>, <c>stack</c>, as text or
/// a list). A line that isn't an object, or has no message, reads as text.
/// </summary>
/// <param name="First">How its first line shows: <c>[time] Category: Error: message</c>, which <see cref="Columns"/> lay out.</param>
/// <param name="More">Its message's other lines.</param>
/// <param name="Trace">Its stack trace's lines.</param>
public sealed record UnityJsonLine(string First, IReadOnlyList<string> More, IReadOnlyList<string> Trace, LogVerbosity Verbosity, string? Category, LogColumns Columns)
{
    private static readonly string[] TimeNames = ["timestamp", "time", "ts", "@t", "dateTime", "date"];
    private static readonly string[] LevelNames = ["level", "severity", "logLevel", "logType", "type", "@l", "lvl"];
    private static readonly string[] MessageNames = ["message", "msg", "text", "@m", "condition", "content"];
    private static readonly string[] CategoryNames = ["category", "channel", "tag", "logger", "subsystem", "source"];
    private static readonly string[] TraceNames = ["stackTrace", "stacktrace", "stack", "callstack", "trace", "@x"];

    /// <summary>The entry a line holds, its time read in <paramref name="zone"/>; null for a line that isn't one.</summary>
    public static UnityJsonLine? Read(string line, TimeZoneInfo zone)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith('{'))
        {
            return null;
        }
        JsonObject entry;
        try
        {
            if (JsonNode.Parse(trimmed) is not JsonObject parsed)
            {
                return null;
            }
            entry = parsed;
        }
        catch (JsonException)
        {
            return null;
        }
        if (Text(entry, MessageNames) is not { } message)
        {
            return null;
        }
        var verbosity = LevelOf(Text(entry, LevelNames));
        var category = Text(entry, CategoryNames) is { Length: > 0 } named ? named.ReplaceLineEndings(" ") : null;
        var rawTime = Text(entry, TimeNames)?.ReplaceLineEndings(" ");
        var messageLines = message.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

        // [time] Category: Tag: message, which the columns lay out as Unreal's are; the text is what's copied and found.
        var prefix = rawTime is null ? "" : $"[{rawTime}] ";
        var categoryPart = category is null ? "" : category + ": ";
        var tag = verbosity switch
        {
            LogVerbosity.Error => "Error",
            LogVerbosity.Warning => "Warning",
            _ => null,
        };
        var tagStart = tag is null ? -1 : prefix.Length + categoryPart.Length;
        var messageStart = prefix.Length + categoryPart.Length + (tag is null ? 0 : tag.Length + 2);
        var first = prefix + categoryPart + (tag is null ? "" : tag + ": ") + messageLines[0];
        // Unity's own time and thread before the message, with -timestamps: the time column has the entry's time, so
        // they're drawn as the thread column, and the message starts after them.
        var stamp = rawTime is null ? null : UnityTimestamp.Read(messageLines[0]);
        var columns = new LogColumns(prefix.Length, rawTime is null ? null : ShownTime(rawTime, zone), category?.Length ?? 0, tagStart, tag?.Length ?? 0,
            messageStart + (stamp?.Length ?? 0), stamp is null ? -1 : messageStart, stamp?.Length ?? 0, stamp?.Thread);
        return new UnityJsonLine(first, messageLines[1..], TraceOf(entry), verbosity, category, columns);
    }

    /// <summary>Error for Unity's Error, Exception and Assert, and the like; Warning for a warning; the rest are messages.</summary>
    private static LogVerbosity LevelOf(string? level) => level?.ToLowerInvariant() switch
    {
        null => LogVerbosity.Log,
        var l when l.Contains("error", StringComparison.Ordinal) || l.Contains("exception", StringComparison.Ordinal) || l.Contains("assert", StringComparison.Ordinal)
            || l.Contains("fatal", StringComparison.Ordinal) || l.Contains("critical", StringComparison.Ordinal) => LogVerbosity.Error,
        var l when l.Contains("warn", StringComparison.Ordinal) => LogVerbosity.Warning,
        _ => LogVerbosity.Log,
    };

    /// <summary>The time as the column shows it, <c>15:01:22.123</c> in <paramref name="zone"/>; as it's written when it isn't a time.</summary>
    internal static string ShownTime(string raw, TimeZoneInfo zone)
    {
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
        {
            return TimeZoneInfo.ConvertTime(time, zone).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            // Seconds or milliseconds since 1970.
            var at = number > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(number) : DateTimeOffset.FromUnixTimeSeconds(number);
            return TimeZoneInfo.ConvertTime(at, zone).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
        return raw.Length > 12 ? raw[..12] : raw;
    }

    /// <summary>The stack trace's lines, from text or a list, without blank ones.</summary>
    private static IReadOnlyList<string> TraceOf(JsonObject entry)
    {
        if (Node(entry, TraceNames) is not { } trace)
        {
            return [];
        }
        IEnumerable<string> lines = trace switch
        {
            JsonArray frames => frames.Select(f => f is JsonValue value && value.TryGetValue<string>(out var text) ? text : f?.ToJsonString() ?? ""),
            JsonValue value when value.TryGetValue<string>(out var text) => text.ReplaceLineEndings("\n").Split('\n'),
            _ => [],
        };
        return [.. lines.Where(l => !string.IsNullOrWhiteSpace(l))];
    }

    /// <summary>The first of <paramref name="names"/> the entry has, ignoring case, as text: a string, or a number as written.</summary>
    private static string? Text(JsonObject entry, string[] names) => Node(entry, names) switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value => value.ToJsonString(),
        _ => null,
    };

    private static JsonNode? Node(JsonObject entry, string[] names)
    {
        foreach (var name in names)
        {
            foreach (var (key, value) in entry)
            {
                if (value is not null && string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
        }
        return null;
    }
}
