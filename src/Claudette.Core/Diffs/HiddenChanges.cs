using Claudette.Core.ProjectFiles;

namespace Claudette.Core.Diffs;

/// <summary>A change to a file's line endings: from what to what, each null when it isn't known.</summary>
public sealed record LineEndingChange(LineEndingKind? From, LineEndingKind? To);

/// <summary>A change to a file's mode, as git writes it (<c>100644</c>, <c>100755</c>, <c>120000</c>).</summary>
public sealed record ModeChange(string From, string To)
{
    /// <summary>It's a plain file on both sides, and only whether it's executable changed.</summary>
    public bool? NowExecutable => (From, To) switch
    {
        ("100644", "100755") => true,
        ("100755", "100644") => false,
        _ => null,
    };
}

/// <summary>
/// What changed in a file that its lines don't show (DESIGN.md §8, "What the lines don't show"): the diff compares
/// lines, so a file whose line endings, byte order mark or newline at the end changed looks the same on both sides,
/// and so does a line whose only change is whitespace.
/// </summary>
public sealed record HiddenChanges
{
    /// <summary>Its line endings changed.</summary>
    public LineEndingChange? LineEndings { get; init; }

    /// <summary>A byte order mark was added (true) or removed (false); null when that didn't change.</summary>
    public bool? ByteOrderMark { get; init; }

    /// <summary>A newline at the end was added (true) or removed (false); null when that didn't change.</summary>
    public bool? NewlineAtEnd { get; init; }

    /// <summary>Lines changed, but only in their whitespace: with it taken out, both sides are the same.</summary>
    public bool WhitespaceOnly { get; init; }

    /// <summary>Its mode changed (git's view only).</summary>
    public ModeChange? Mode { get; init; }

    /// <summary>
    /// What changed between <paramref name="before"/> and <paramref name="after"/> that their lines don't show, or null
    /// when nothing did. Either text null (the file wasn't there) has nothing to compare. A text's byte order mark is a
    /// leading <c>﻿</c>, as Claude Code's <c>originalFile</c> and <see cref="TextFiles"/> keep it.
    /// </summary>
    /// <param name="beforeLineEndings">
    /// The line endings the file really had before, when <paramref name="before"/> doesn't have them: Claude Code reports
    /// a file with Windows line endings with <c>\n</c>. Null when they aren't known, so a change to them isn't told.
    /// </param>
    /// <param name="afterLineEndings">The same for <paramref name="after"/>.</param>
    public static HiddenChanges? Between(string? before, LineEndingKind? beforeLineEndings, string? after, LineEndingKind? afterLineEndings)
    {
        if (before is null || after is null || before.Length == 0 && after.Length == 0)
        {
            return null;
        }
        var lineEndings = beforeLineEndings is { } from && afterLineEndings is { } to && from != to
            && from != LineEndingKind.None && to != LineEndingKind.None
            ? new LineEndingChange(from, to)
            : null;
        var markBefore = HasMark(before);
        var markAfter = HasMark(after);
        var newlineBefore = EndsWithNewline(before);
        var newlineAfter = EndsWithNewline(after);
        var changes = new HiddenChanges
        {
            LineEndings = lineEndings,
            ByteOrderMark = markBefore == markAfter ? null : markAfter,
            // An empty file has no last line to end.
            NewlineAtEnd = newlineBefore == newlineAfter || Content(before).Length == 0 || Content(after).Length == 0 ? null : newlineAfter,
            WhitespaceOnly = LineDiff.DiffersOnlyInWhitespace(before, after),
        };
        return changes.Any ? changes : null;
    }

    /// <summary>Anything changed.</summary>
    public bool Any => LineEndings is not null || ByteOrderMark is not null || NewlineAtEnd is not null || WhitespaceOnly || Mode is not null;

    /// <summary>Whether <paramref name="text"/> starts with a byte order mark.</summary>
    public static bool HasMark(string text) => text.Length > 0 && text[0] == '﻿';

    private static string Content(string text) => HasMark(text) ? text[1..] : text;

    private static bool EndsWithNewline(string text) => text.Length > 0 && text[^1] is '\n' or '\r';
}
