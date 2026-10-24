using System.Text;
using System.Text.Json.Nodes;
using Claudette.Core.Protocol;

namespace Claudette.Core.Diffs;

/// <summary>
/// What a file tool's call left the file holding (DESIGN.md §8, "This session's changes"), worked out from its input
/// and result rather than read from the disk, so it's the same live and when a transcript is read back, and another
/// tab writing the file at the same moment can't get into it.
/// </summary>
public static class FileAfter
{
    /// <summary>
    /// The file after the call. <paramref name="base"/> is what the call changed: its own <c>originalFile</c>, or how
    /// the session's previous change left the file; null for a file that didn't exist.
    /// </summary>
    /// <returns>The text, and whether it's known: an Edit to an unknown base, or one that doesn't fit it, isn't.</returns>
    public static (string? After, bool Known) Of(string toolName, JsonObject? input, JsonObject? result, string? @base, bool baseKnown)
    {
        switch (toolName)
        {
            case "Write":
                return (input?.GetString("content") ?? result?.GetString("content")) is { } content ? (content, true) : (null, false);
            case "NotebookEdit":
                return result?.GetString("updated_file") is { } notebook ? (notebook, true) : (null, false);
            case "Edit" when baseKnown:
                var oldString = result?.GetString("oldString") ?? input?.GetString("old_string");
                var newString = result?.GetString("newString") ?? input?.GetString("new_string");
                var replaceAll = result?.GetBool("replaceAll") ?? input?.GetBool("replace_all") ?? false;
                if (oldString is { Length: 0 } && newString is not null && string.IsNullOrEmpty(@base))
                {
                    return (newString, true); // An Edit with nothing to replace creates the file.
                }
                if (oldString is { Length: > 0 } && newString is not null && Replace(@base ?? "", oldString, newString, replaceAll) is { } replaced)
                {
                    return (replaced, true);
                }
                return Patched(@base, result);
            case "MultiEdit" when baseKnown:
                var text = @base ?? "";
                foreach (var edit in (input?["edits"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (edit.GetString("old_string") is not { } from || edit.GetString("new_string") is not { } to)
                    {
                        return Patched(@base, result);
                    }
                    if (from.Length == 0 && text.Length == 0)
                    {
                        text = to;
                        continue;
                    }
                    if (from.Length == 0 || Replace(text, from, to, edit.GetBool("replace_all") ?? false) is not { } next)
                    {
                        return Patched(@base, result);
                    }
                    text = next;
                }
                return (text, true);
            default:
                return (null, false);
        }
    }

    /// <summary>
    /// <paramref name="text"/> with <paramref name="from"/> replaced, once or everywhere, or null when it isn't there.
    /// A file with Windows line endings is matched with them too, since the model writes <c>\n</c>.
    /// </summary>
    internal static string? Replace(string text, string from, string to, bool all)
    {
        if (Once(text, from, to, all) is { } replaced)
        {
            return replaced;
        }
        if (text.Contains("\r\n", StringComparison.Ordinal) && from.Contains('\n') && !from.Contains("\r\n", StringComparison.Ordinal))
        {
            return Once(text, from.Replace("\n", "\r\n", StringComparison.Ordinal), to.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal), all);
        }
        return null;

        static string? Once(string text, string from, string to, bool all)
        {
            var at = text.IndexOf(from, StringComparison.Ordinal);
            if (at < 0)
            {
                return null;
            }
            return all ? text.Replace(from, to, StringComparison.Ordinal) : string.Concat(text.AsSpan(0, at), to, text.AsSpan(at + from.Length));
        }
    }

    /// <summary>The result's <c>structuredPatch</c> applied to <paramref name="base"/>, when its lines fit it.</summary>
    private static (string? After, bool Known) Patched(string? @base, JsonObject? result) =>
        result?["structuredPatch"] is JsonArray { Count: > 0 } hunks && ApplyPatch(@base ?? "", hunks) is { } patched ? (patched, true) : (null, false);

    /// <summary>
    /// Applies a jsdiff <c>structuredPatch</c> (hunks of <c>" "</c>, <c>"-"</c> and <c>"+"</c> lines), checking each
    /// context and removed line against <paramref name="text"/>, line endings aside. Null when one doesn't match: the
    /// patch was made to another text. Added lines take the file's line ending.
    /// </summary>
    internal static string? ApplyPatch(string text, JsonArray hunks)
    {
        var lines = SplitLines(text);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var output = new StringBuilder(text.Length + 256);
        var next = 0;
        foreach (var hunk in hunks.OfType<JsonObject>())
        {
            if (hunk["oldStart"] is not JsonValue start1 || !start1.TryGetValue<int>(out var oldStart) || hunk["lines"] is not JsonArray hunkLines)
            {
                return null;
            }
            var oldLines = hunk["oldLines"] is JsonValue count && count.TryGetValue<int>(out var n) ? n : 0;
            // As diff -u writes it, a hunk with no old lines starts after the line it names.
            var start = oldLines == 0 ? oldStart : oldStart - 1;
            if (start < next || start > lines.Count)
            {
                return null;
            }
            for (; next < start; next++)
            {
                output.Append(lines[next].Text).Append(lines[next].Ending);
            }
            foreach (var node in hunkLines)
            {
                if (node is not JsonValue value || !value.TryGetValue<string>(out var line))
                {
                    return null;
                }
                if (line.Length == 0 || line[0] == '\\')
                {
                    continue; // "\ No newline at end of file"
                }
                var body = line[1..];
                switch (line[0])
                {
                    case ' ' or '-':
                        if (next >= lines.Count || !string.Equals(lines[next].Text, body.TrimEnd('\r'), StringComparison.Ordinal))
                        {
                            return null;
                        }
                        if (line[0] == ' ')
                        {
                            output.Append(lines[next].Text).Append(lines[next].Ending);
                        }
                        next++;
                        break;
                    case '+':
                        output.Append(body.TrimEnd('\r')).Append(newline);
                        break;
                    default:
                        return null;
                }
            }
        }
        for (; next < lines.Count; next++)
        {
            output.Append(lines[next].Text).Append(lines[next].Ending);
        }
        return output.ToString();
    }

    /// <summary>The lines of <paramref name="text"/>, each with the line ending it has (none on a last line without one).</summary>
    private static List<(string Text, string Ending)> SplitLines(string text)
    {
        var lines = new List<(string, string)>();
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOfAny(['\r', '\n'], start);
            if (end < 0)
            {
                lines.Add((text[start..], ""));
                break;
            }
            var ending = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? "\r\n" : text[end].ToString();
            lines.Add((text[start..end], ending));
            start = end + ending.Length;
        }
        return lines;
    }

    /// <summary>Whether two texts are the same, line endings aside.</summary>
    public static bool Same(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }
        return string.Equals(ToLf(a), ToLf(b), StringComparison.Ordinal);

        static string ToLf(string text) => text.Contains('\r') ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') : text;
    }
}
