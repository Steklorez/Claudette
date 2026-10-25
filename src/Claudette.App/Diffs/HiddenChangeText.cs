using Claudette.Core;
using Claudette.Core.Diffs;
using Claudette.Core.ProjectFiles;

namespace Claudette.App.Diffs;

/// <summary>
/// How Changed files, the Modified files card and the diff view say what changed in a file that its lines don't show
/// (DESIGN.md §8, "What the lines don't show").
/// </summary>
public static class HiddenChangeText
{
    /// <summary>
    /// The line under a Changed files row's folder, and the diff view's note: <i>"Only its line endings changed: CRLF to
    /// LF"</i>, or with lines changed as well, <i>"Its line endings changed too: CRLF to LF"</i>. Null when nothing did.
    /// </summary>
    /// <param name="linesChanged">Lines changed too, other than in their whitespace.</param>
    /// <param name="fromGit">Compared with HEAD, whose line endings git may say nothing about.</param>
    public static string? Note(HiddenChanges? hidden, bool linesChanged, bool fromGit = false)
    {
        var items = Items(hidden, fromGit);
        if (items.Count == 0)
        {
            return null;
        }
        if (items.Count == 1)
        {
            var (name, detail) = items[0];
            var said = linesChanged ? $"{Formats.Capitalize(name)} changed too" : $"Only {name} changed";
            return detail is null ? said : $"{said}: {detail}";
        }
        var list = Formats.List(items.Select(i => i.Detail is null ? i.Name : $"{i.Name} ({i.Detail})").ToArray());
        return linesChanged ? $"{Formats.Capitalize(list)} changed too" : $"Only {list} changed";
    }

    /// <summary>
    /// What a Modified files tile shows in place of <c>+0 −0</c> when no line changed: <c>CRLF → LF</c>,
    /// <c>whitespace</c>, <c>byte order mark</c>, <c>newline at end</c>.
    /// </summary>
    public static string? Short(HiddenChanges? hidden)
    {
        if (hidden is null)
        {
            return null;
        }
        var parts = new List<string>();
        if (hidden.WhitespaceOnly)
        {
            parts.Add("whitespace");
        }
        if (hidden.LineEndings is { } endings)
        {
            parts.Add(endings is { From: { } from, To: { } to } ? $"{Label(from)} → {Label(to)}" : "line endings");
        }
        if (hidden.ByteOrderMark is not null)
        {
            parts.Add("byte order mark");
        }
        if (hidden.NewlineAtEnd is not null)
        {
            parts.Add("newline at end");
        }
        if (hidden.Mode is not null)
        {
            parts.Add("mode");
        }
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>What changed, each with its detail when there's one to give.</summary>
    private static List<(string Name, string? Detail)> Items(HiddenChanges? hidden, bool fromGit)
    {
        var items = new List<(string, string?)>();
        if (hidden is null)
        {
            return items;
        }
        if (hidden.WhitespaceOnly)
        {
            items.Add(("whitespace", null));
        }
        if (hidden.LineEndings is { } endings)
        {
            items.Add(endings is { From: { } from, To: { } to } ? ("its line endings", $"{Label(from)} to {Label(to)}")
                // Git counts them as changed without saying how: maybe they're as they were checked out, and git would
                // commit them with others.
                : fromGit ? ("its line endings", "they differ from HEAD's") : ("its line endings", null));
        }
        if (hidden.ByteOrderMark is { } added)
        {
            items.Add(("its byte order mark", added ? "added" : "removed"));
        }
        if (hidden.NewlineAtEnd is { } newline)
        {
            items.Add(("the newline at its end", newline ? "added" : "removed"));
        }
        if (hidden.Mode is { } mode)
        {
            items.Add(("its mode", mode.NowExecutable switch
            {
                true => "executable now",
                false => "not executable now",
                _ => $"{mode.From} to {mode.To}",
            }));
        }
        return items;
    }

    private static string Label(LineEndingKind kind) => kind switch
    {
        LineEndingKind.Crlf => "CRLF",
        LineEndingKind.Lf => "LF",
        LineEndingKind.Mixed => "mixed",
        _ => "none",
    };
}
