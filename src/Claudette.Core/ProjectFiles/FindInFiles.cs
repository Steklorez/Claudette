using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Claudette.Core.Diffs;

namespace Claudette.Core.ProjectFiles;

/// <summary>A match Find in files found (DESIGN.md §18, "Project files", "Finding in files").</summary>
/// <param name="Path">The file's full path.</param>
/// <param name="RelativePath">Its path from the folder whose files were listed, with forward slashes.</param>
/// <param name="Line">Its line, 1-based, counting as the editor does: <c>\r\n</c>, <c>\n</c> and <c>\r</c> each end one.</param>
/// <param name="Column">Where it starts in the line, 1-based, in UTF-16 code units as the editor counts.</param>
/// <param name="Length">How long it is.</param>
/// <param name="LineText">The line, or for one over <see cref="FindInFiles.LongestLineText"/>, the part of it around the match.</param>
/// <param name="LineStart">Where <paramref name="LineText"/> starts in the line: 0 unless the line was cut.</param>
public sealed record FindHit(string Path, string RelativePath, int Line, int Column, int Length, string LineText, int LineStart = 0)
{
    /// <summary>Where the match starts in <see cref="LineText"/>.</summary>
    public int MatchStart => Column - 1 - LineStart;
}

/// <summary>A file Find in files found matches in, and how many.</summary>
public sealed record FindFileMatches(string Path, string RelativePath, int Count);

/// <summary>What to look for, and where (DESIGN.md §18, "Project files", "Finding in files").</summary>
/// <param name="Root">The folder <paramref name="Files"/> are in.</param>
/// <param name="Files">The files to look through, relative to <paramref name="Root"/> with forward slashes, as the <c>@</c> list has them (folders, ending in a slash, are passed over).</param>
public sealed record FindRequest(string Root, IReadOnlyList<string> Files, FindMatcher Matcher)
{
    /// <summary>Only the files under this folder, relative to <see cref="Root"/> with a slash at its end; empty for all.</summary>
    public string Under { get; init; } = "";

    public FileMask Mask { get; init; } = FileMask.All;

    /// <summary>The text of files open in an editor, by full path, which is looked in instead of what's on disk.</summary>
    public IReadOnlyDictionary<string, string> OpenTexts { get; init; } = new Dictionary<string, string>();

    /// <summary>At most this many matches are kept; the rest are counted.</summary>
    public int MostHits { get; init; } = FindInFiles.MostHits;

    /// <summary>How many files are read at once.</summary>
    public int Parallelism { get; init; } = Math.Clamp(Environment.ProcessorCount, 1, 8);
}

/// <summary>
/// Matches found since the last batch, in order (by path, then line), with the counts so far. The last batch, with
/// <see cref="IsComplete"/>, has the final counts.
/// </summary>
/// <param name="Hits">The matches to add, until <see cref="FindRequest.MostHits"/> are kept.</param>
/// <param name="Files">The files done since the last batch that have matches, with how many each has.</param>
/// <param name="Matches">Every match so far, kept or not.</param>
/// <param name="FilesWithMatches">How many files have matches so far.</param>
/// <param name="FilesSearched">How many of the files to look in are done.</param>
/// <param name="FilesToSearch">How many files there are to look in.</param>
/// <param name="IsCapped">There were more matches than are kept.</param>
/// <param name="TimedOut">Files skipped because the regular expression took too long on a line of theirs.</param>
public sealed record FindBatch(IReadOnlyList<FindHit> Hits, IReadOnlyList<FindFileMatches> Files, int Matches, int FilesWithMatches, int FilesSearched,
    int FilesToSearch, bool IsCapped, int TimedOut, bool IsComplete);

/// <summary>
/// Find in files (DESIGN.md §18, "Project files", "Finding in files"): what's in a project's text files, line by line,
/// read in parallel off the caller's thread and handed back in batches, in path order. Binary files and files over
/// <see cref="TextFile.MaxBytes"/> are passed over, as the editor passes them over; an open file is looked in as its
/// editor has it.
/// </summary>
public static class FindInFiles
{
    /// <summary>How many matches are kept; the rest are counted.</summary>
    public const int MostHits = 1_000;

    /// <summary>A line longer than this keeps only the part around a match.</summary>
    public const int LongestLineText = 1_000;

    /// <summary>The files <paramref name="request"/> looks in: under its folder and matching its mask, in order.</summary>
    public static IReadOnlyList<string> FilesToSearch(FindRequest request) =>
        request.Files.Where(f => !f.EndsWith('/') && f.StartsWith(request.Under, StringComparison.Ordinal) && request.Mask.Matches(f)).ToArray();

    /// <summary>
    /// Looks through the request's files, handing back batches as files are done, in path order: the matches to add and
    /// the counts so far, then a last batch with <see cref="FindBatch.IsComplete"/>. Cancelling stops the reading.
    /// </summary>
    public static async IAsyncEnumerable<FindBatch> SearchAsync(FindRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var files = FilesToSearch(request);
        var results = Channel.CreateUnbounded<(int Index, FileResult Result)>(new UnboundedChannelOptions { SingleReader = true });
        // On a thread of its own: Parallel.For blocks it, and each file is read with blocking IO. Always started, so the
        // channel is always completed, even once cancelled.
        _ = Task.Factory.StartNew(() =>
        {
            try
            {
                Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = request.Parallelism, CancellationToken = cancellationToken },
                    i => results.Writer.TryWrite((i, SearchFile(request, files[i], request.MostHits))));
                results.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                results.Writer.TryComplete(ex);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        var reader = results.Reader;
        var done = new Dictionary<int, FileResult>();
        var next = 0;
        var kept = 0;
        var matches = 0;
        var withMatches = 0;
        var timedOut = 0;
        var capped = false;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
            {
                done[item.Index] = item.Result;
            }
            // In order: a file is handed back once those before it are.
            var hits = new List<FindHit>();
            var found = new List<FindFileMatches>();
            var from = next;
            while (done.Remove(next, out var result))
            {
                next++;
                if (result.TimedOut)
                {
                    timedOut++;
                }
                if (result.Count == 0)
                {
                    continue;
                }
                matches += result.Count;
                withMatches++;
                found.Add(new FindFileMatches(result.Path, result.RelativePath, result.Count));
                var room = request.MostHits - kept;
                hits.AddRange(result.Hits.Take(room));
                kept += Math.Min(room, result.Hits.Count);
                capped |= result.Count > room;
            }
            if (next > from)
            {
                yield return new FindBatch(hits, found, matches, withMatches, next, files.Count, capped, timedOut, IsComplete: false);
            }
        }
        yield return new FindBatch([], [], matches, withMatches, next, files.Count, capped, timedOut, IsComplete: true);
    }

    /// <summary>
    /// The matches in <paramref name="text"/>, the file at <paramref name="path"/>, in order: at most
    /// <paramref name="most"/>, with <paramref name="count"/> all of them.
    /// </summary>
    /// <exception cref="RegexMatchTimeoutException">The regular expression took too long on a line.</exception>
    public static IReadOnlyList<FindHit> Search(string path, string relativePath, string text, FindMatcher matcher, int most, out int count)
    {
        count = 0;
        var hits = new List<FindHit>();
        // Most files don't hold plain text at all: one look at the whole of it, before taking it apart.
        if (!matcher.Query.IsRegex && !matcher.MayMatch(text))
        {
            return hits;
        }
        var found = new List<(int Start, int Length)>();
        foreach (var (number, start, length) in Lines(text))
        {
            if (!matcher.MayMatch(text.AsSpan(start, length)))
            {
                continue;
            }
            var line = text.Substring(start, length);
            found.Clear();
            matcher.Find(line, found);
            foreach (var (at, matched) in found)
            {
                count++;
                if (hits.Count < most)
                {
                    var (shown, shownStart) = LineText(line, at, matched);
                    hits.Add(new FindHit(path, relativePath, number, at + 1, matched, shown, shownStart));
                }
            }
        }
        return hits;
    }

    /// <summary>
    /// Each line of <paramref name="text"/>: its number (1-based), where it starts and its length without its ending.
    /// Lines end as the editor ends them, at <c>\r\n</c>, <c>\n</c> or <c>\r</c>, and a text ending in one has an empty
    /// last line.
    /// </summary>
    public static IEnumerable<(int Number, int Start, int Length)> Lines(string text)
    {
        var number = 0;
        var start = 0;
        while (true)
        {
            number++;
            var end = text.AsSpan(start).IndexOfAny('\r', '\n');
            var length = end < 0 ? text.Length - start : end;
            yield return (number, start, length);
            if (end < 0)
            {
                yield break;
            }
            var next = start + length + 1;
            if (text[start + length] == '\r' && next < text.Length && text[next] == '\n')
            {
                next++;
            }
            start = next;
        }
    }

    /// <summary>The line to keep with a match: all of it, or for a long one the part around the match.</summary>
    private static (string Text, int Start) LineText(string line, int at, int length)
    {
        if (line.Length <= LongestLineText)
        {
            return (line, 0);
        }
        var start = Math.Max(0, at - 200);
        var end = Math.Min(line.Length, at + Math.Min(length, 2_000) + 300);
        return (line[start..end], start);
    }

    private static FileResult SearchFile(FindRequest request, string relative, int most)
    {
        var path = Path.GetFullPath(Path.Combine(request.Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string text;
        if (request.OpenTexts.TryGetValue(path, out var open))
        {
            text = open;
        }
        else
        {
            // Binary, over 10 MB, gone or not to be read: passed over, as the editor passes them over.
            var content = TextFile.Read(path);
            if (content.Problem != TextFileProblem.None)
            {
                return new FileResult(path, relative, [], 0, TimedOut: false);
            }
            text = content.Text;
        }
        try
        {
            var hits = Search(path, relative, text, request.Matcher, most, out var count);
            return new FileResult(path, relative, hits, count, TimedOut: false);
        }
        catch (RegexMatchTimeoutException)
        {
            return new FileResult(path, relative, [], 0, TimedOut: true);
        }
    }

    private sealed record FileResult(string Path, string RelativePath, IReadOnlyList<FindHit> Hits, int Count, bool TimedOut);

    /// <summary>Paths compared as the OS compares them: ignoring case on Windows.</summary>
    public static StringComparer PathComparer => ChangedFiles.PlatformPathComparer;
}
