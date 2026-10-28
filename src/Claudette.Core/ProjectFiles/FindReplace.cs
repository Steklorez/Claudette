using System.Text;
using System.Text.RegularExpressions;

namespace Claudette.Core.ProjectFiles;

/// <summary>A change to a text: <paramref name="Length"/> characters at <paramref name="Offset"/> become <paramref name="Text"/>.</summary>
public sealed record TextEdit(int Offset, int Length, string Text);

/// <summary>What Replace in files replaces in one file (DESIGN.md §18, "Project files", "Finding in files").</summary>
/// <param name="Path">The file's full path.</param>
/// <param name="RelativePath">Its path as the search listed it.</param>
public sealed record ReplaceTarget(string Path, string RelativePath)
{
    /// <summary>
    /// The file's matches as they were listed, for knowing whether it changed since: null to replace whatever matches
    /// now.
    /// </summary>
    public IReadOnlyList<FindHit>? Found { get; init; }

    /// <summary>Of the matches listed, those to leave as they are.</summary>
    public IReadOnlyCollection<FindHit> Excluded { get; init; } = [];

    /// <summary>Only this match, if it's still where it was listed.</summary>
    public FindHit? Only { get; init; }
}

/// <summary>The edits replacing makes in one file, or why it makes none.</summary>
public sealed record ReplacePlan(IReadOnlyList<TextEdit> Edits, string? Problem)
{
    public int Count => Edits.Count;
}

/// <summary>How replacing in one file went: how many it replaced, or why it couldn't.</summary>
public sealed record FileReplaceResult(string Path, string RelativePath, int Replaced, string? Problem);

/// <summary>
/// Replace in files (DESIGN.md §18, "Project files", "Finding in files"). A file is always searched again just before
/// it's replaced, so a replacement never lands where a match used to be: one that changed since it was listed has its
/// matches found anew, and a match asked for alone, or a file with matches left out, is only replaced where the
/// matches are still those listed. A file on disk is written through <see cref="TextFile"/>, keeping its encoding,
/// byte order mark and line endings, after <see cref="IBeforeFilesWrite"/> has got it ready.
/// </summary>
public static class FindReplace
{
    /// <summary>The edits replacing makes in <paramref name="text"/>, the text of <paramref name="target"/>'s file now.</summary>
    public static ReplacePlan Plan(string text, ReplaceTarget target, FindMatcher matcher, string replacement)
    {
        IReadOnlyList<FindHit> now;
        try
        {
            now = FindInFiles.Search(target.Path, target.RelativePath, text, matcher, int.MaxValue, out _);
        }
        catch (RegexMatchTimeoutException)
        {
            return new ReplacePlan([], "The regular expression took too long on one of its lines.");
        }
        IReadOnlyList<FindHit> chosen;
        if (target.Only is { } only)
        {
            if (!now.Contains(only))
            {
                return new ReplacePlan([], "It changed since it was searched, so that match isn't where it was.");
            }
            chosen = [only];
        }
        else if (target.Excluded.Count > 0)
        {
            // What's left out was picked from the list: it's only known where it is while the matches are those listed.
            if (target.Found is { } found && !now.Take(found.Count).SequenceEqual(found))
            {
                return new ReplacePlan([], "It changed since it was searched, so nothing in it was replaced. Search again to see its matches.");
            }
            var excluded = target.Excluded.ToHashSet();
            chosen = [.. now.Where(h => !excluded.Contains(h))];
        }
        else
        {
            chosen = now;
        }
        return new ReplacePlan(Edits(text, chosen, matcher, replacement), null);
    }

    /// <summary>The edits that put <paramref name="replacement"/> in place of each of <paramref name="hits"/>, matches in <paramref name="text"/>.</summary>
    public static IReadOnlyList<TextEdit> Edits(string text, IReadOnlyList<FindHit> hits, FindMatcher matcher, string replacement)
    {
        if (hits.Count == 0)
        {
            return [];
        }
        var lines = FindInFiles.Lines(text).ToList();
        var edits = new List<TextEdit>(hits.Count);
        string? lineText = null;
        var lineNumber = 0;
        foreach (var hit in hits.OrderBy(h => h.Line).ThenBy(h => h.Column))
        {
            var (_, start, length) = lines[hit.Line - 1];
            if (lineNumber != hit.Line)
            {
                lineNumber = hit.Line;
                lineText = text.Substring(start, length);
            }
            edits.Add(new TextEdit(start + hit.Column - 1, hit.Length, matcher.Replacement(lineText!, hit.Column - 1, hit.Length, replacement)));
        }
        return edits;
    }

    /// <summary><paramref name="text"/> with <paramref name="edits"/> made, which don't overlap.</summary>
    public static string Apply(string text, IReadOnlyList<TextEdit> edits)
    {
        var result = new StringBuilder(text.Length);
        var at = 0;
        foreach (var edit in edits.OrderBy(e => e.Offset))
        {
            result.Append(text, at, edit.Offset - at).Append(edit.Text);
            at = edit.Offset + edit.Length;
        }
        return result.Append(text, at, text.Length - at).ToString();
    }

    /// <summary>
    /// Replaces in a file on disk, as <paramref name="target"/> says: read, searched again, and written back in its
    /// encoding, byte order mark and line endings, only if it's still what was read.
    /// </summary>
    public static FileReplaceResult InFile(ReplaceTarget target, FindMatcher matcher, string replacement)
    {
        var content = TextFile.Read(target.Path);
        var problem = content.Problem switch
        {
            TextFileProblem.Missing => "It isn't there any more.",
            TextFileProblem.Binary => "It isn't text.",
            TextFileProblem.TooLarge => "It's over 10 MB.",
            TextFileProblem.Unreadable => "It couldn't be read: another program may have it open, or Claudette isn't allowed to.",
            _ => null,
        };
        if (problem is null && content.Encoding is null)
        {
            problem = "It isn't UTF-8 or UTF-16 text, so it can't be written back as it was.";
        }
        if (problem is null && !CanWrite(target.Path))
        {
            problem = "It's read-only.";
        }
        if (problem is not null)
        {
            return new FileReplaceResult(target.Path, target.RelativePath, 0, problem);
        }
        var plan = Plan(content.Text, target, matcher, replacement);
        if (plan.Problem is not null || plan.Count == 0)
        {
            return new FileReplaceResult(target.Path, target.RelativePath, 0, plan.Problem);
        }
        try
        {
            if (!TextFile.Save(target.Path, content.Text, Apply(content.Text, plan.Edits)))
            {
                return new FileReplaceResult(target.Path, target.RelativePath, 0, "It changed while it was being replaced, so it was left as it is.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileReplaceResult(target.Path, target.RelativePath, 0, ex.Message);
        }
        return new FileReplaceResult(target.Path, target.RelativePath, plan.Count, null);
    }

    /// <summary>
    /// Replaces in files on disk, off the caller's thread: <paramref name="beforeWrite"/> gets them ready first, all at
    /// once, and those it can't are left as they are, with its reason.
    /// </summary>
    public static async Task<IReadOnlyList<FileReplaceResult>> OnDiskAsync(IReadOnlyList<ReplaceTarget> targets, FindMatcher matcher, string replacement,
        IBeforeFilesWrite beforeWrite, CancellationToken cancellationToken = default)
    {
        if (targets.Count == 0)
        {
            return [];
        }
        var prepared = await beforeWrite.PrepareAsync([.. targets.Select(t => t.Path)], cancellationToken).ConfigureAwait(false);
        var refused = new Dictionary<string, string>(prepared, FindInFiles.PathComparer);
        return await Task.Factory.StartNew(
            () => targets.Select(t => refused.TryGetValue(t.Path, out var why) ? new FileReplaceResult(t.Path, t.RelativePath, 0, why) : InFile(t, matcher, replacement)).ToArray(),
            cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);
    }

    private static bool CanWrite(string path)
    {
        try
        {
            return TextFile.CanWrite(new FileInfo(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>
/// What happens before Claudette writes files that aren't open in an editor (DESIGN.md §18, "Project files", "Finding in
/// files"): the one place a workspace that keeps its files read-only until they're opened for edit, as Perforce does,
/// can check them out first. Replace in files calls it once with every file it's about to write. Until something needs
/// it, <see cref="NothingBeforeFilesWrite"/> does nothing.
/// </summary>
public interface IBeforeFilesWrite
{
    /// <summary>Gets <paramref name="paths"/> ready to be written. Returns those that can't be, by path, with why.</summary>
    Task<IReadOnlyDictionary<string, string>> PrepareAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken);
}

/// <summary>Nothing to do before files are written: every one is ready.</summary>
public sealed class NothingBeforeFilesWrite : IBeforeFilesWrite
{
    public static NothingBeforeFilesWrite Instance { get; } = new();

    public Task<IReadOnlyDictionary<string, string>> PrepareAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}
