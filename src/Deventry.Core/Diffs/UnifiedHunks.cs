using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Deventry.Core.Diffs;

/// <summary>
/// A unified diff's hunks, as an agent reports a change without the text before and after (Codex's file changes: DESIGN.md
/// §18, "Codex, a second agent"), turned into the hunks of a <c>structuredPatch</c>, which the diff views and
/// <see cref="FileAfter"/> already read: <c>oldStart</c>, <c>oldLines</c>, <c>newStart</c>, <c>newLines</c> and
/// <c>lines</c>, each line with its <c>" "</c>, <c>"-"</c> or <c>"+"</c>.
/// </summary>
public static partial class UnifiedHunks
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")]
    private static partial Regex Header();

    /// <summary>
    /// The hunks in <paramref name="diff"/>; file headers (<c>---</c>, <c>+++</c>, <c>diff --git</c>) before them are
    /// passed over. Empty when it has none.
    /// </summary>
    public static JsonArray ToStructuredPatch(string? diff)
    {
        var hunks = new JsonArray();
        if (string.IsNullOrEmpty(diff))
        {
            return hunks;
        }
        JsonObject? hunk = null;
        JsonArray? lines = null;
        foreach (var line in diff.ReplaceLineEndings("\n").Split('\n'))
        {
            if (Header().Match(line) is { Success: true } match)
            {
                lines = [];
                hunk = new JsonObject
                {
                    ["oldStart"] = Number(match.Groups[1]),
                    ["oldLines"] = match.Groups[2].Success ? Number(match.Groups[2]) : 1,
                    ["newStart"] = Number(match.Groups[3]),
                    ["newLines"] = match.Groups[4].Success ? Number(match.Groups[4]) : 1,
                    ["lines"] = lines,
                };
                hunks.Add(hunk);
                continue;
            }
            // Lines of a hunk; "\ No newline at end of file" says something about the line before, which isn't kept.
            if (lines is not null && line.Length > 0 && line[0] is ' ' or '-' or '+')
            {
                lines.Add(line);
            }
        }
        return hunks;
    }

    /// <summary>
    /// The text before the change, from <paramref name="after"/> and the change's hunks: each hunk taken back, checked
    /// against the text as it goes. Null when a hunk's lines don't fit.
    /// </summary>
    public static string? Before(string after, string? diff)
    {
        var hunks = ToStructuredPatch(diff);
        var reversed = new JsonArray();
        foreach (var hunk in hunks.OfType<JsonObject>())
        {
            var lines = new JsonArray();
            foreach (var line in (hunk["lines"] as JsonArray ?? []).Select(l => l?.GetValue<string>() ?? ""))
            {
                lines.Add(line.Length > 0 && line[0] == '+' ? "-" + line[1..] : line.Length > 0 && line[0] == '-' ? "+" + line[1..] : line);
            }
            reversed.Add(new JsonObject
            {
                ["oldStart"] = hunk["newStart"]?.DeepClone(),
                ["oldLines"] = hunk["newLines"]?.DeepClone(),
                ["newStart"] = hunk["oldStart"]?.DeepClone(),
                ["newLines"] = hunk["oldLines"]?.DeepClone(),
                ["lines"] = lines,
            });
        }
        return FileAfter.ApplyPatch(after, reversed);
    }

    private static int Number(Group group) => int.Parse(group.Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
