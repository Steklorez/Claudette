using System.Text.RegularExpressions;

namespace Deventry.Core.ProjectTools.Unity;

/// <summary>
/// What Unity's <c>-timestamps</c> puts before each line of its log, and before a JSON entry's message (DESIGN.md §18,
/// "Editor logs", "Its lines"): the time in UTC and the thread that wrote it, <c>2026-11-06T12:45:48.327Z|0x9484|</c>.
/// </summary>
/// <param name="Length">The whole of it, to where the message starts.</param>
/// <param name="TimeLength">The time's, with the <c>|</c> after it.</param>
/// <param name="Time">The time, as written.</param>
/// <param name="Thread">The thread, <c>0x9484</c>; null when only the time is there.</param>
internal sealed partial record UnityTimestamp(int Length, int TimeLength, string Time, string? Thread)
{
    /// <summary>The time and thread at the start of <paramref name="text"/>; null when it doesn't start with them.</summary>
    public static UnityTimestamp? Read(string text)
    {
        if (Prefix().Match(text) is not { Success: true } match)
        {
            return null;
        }
        var time = match.Groups["time"];
        var thread = match.Groups["thread"];
        return new UnityTimestamp(match.Length, time.Length + 1, time.Value, thread.Success ? thread.Value : null);
    }

    [GeneratedRegex(@"^(?<time>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z)\|(?:(?<thread>0x[0-9A-Fa-f]+)\|)?")]
    private static partial Regex Prefix();
}
