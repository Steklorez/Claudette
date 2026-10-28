using System.Globalization;
using Claudette.App.Editor;
using Claudette.App.Services;
using Claudette.Core.Composer;
using Claudette.Core.ProjectFiles;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Claudette.App.ViewModels;

/// <summary>What Find in files looks for and where (DESIGN.md §18, "Project files", "Finding in files").</summary>
/// <param name="Project">The project's folder: its group's.</param>
/// <param name="Directory">The folder looked in with <b>Directory</b>, or null for <b>In project</b>.</param>
public sealed record FindSettings(string Project, string Query, bool MatchCase, bool WholeWords, bool IsRegex, string FileMask, string? Directory)
{
    public FindQuery ToQuery() => new(Query, MatchCase, WholeWords, IsRegex);
}

/// <summary>What Find in files needs from the window, which the shell gives it (DESIGN.md §18, "Finding in files").</summary>
internal interface IFindInFilesHost
{
    /// <summary>The text of each file open in a Files tab, by full path: what's looked in in place of the file on disk. On the UI thread.</summary>
    IReadOnlyDictionary<string, string> OpenTexts();

    /// <summary>The text of <paramref name="path"/> in a Files tab, or null when it isn't open there. On the UI thread.</summary>
    string? OpenText(string path);

    /// <summary>The open file's text, to replace in, or null when it isn't open in a Files tab to be edited. On the UI thread.</summary>
    IEditorDocument? OpenDocument(string path);

    /// <summary>Opens a match in its project's Files tab, with the match selected, as a file clicked in a conversation opens.</summary>
    Task OpenHitAsync(FindHit hit);
}

/// <summary>
/// Where Find in files' results are kept: <b>Open in Find window</b> hands them to the Find tool window, docked at the
/// bottom, as a tab of their own titled by the query (DESIGN.md §18, "Finding in files").
/// </summary>
public interface IFindResultsHost
{
    /// <summary>The Find window is there to keep results in; until it is, <b>Open in Find window</b> is hidden.</summary>
    bool CanKeepResults { get; }

    /// <summary>
    /// Shows <paramref name="results"/> in the Find window, docked at the bottom, as a new tab titled by
    /// <see cref="FindResultsViewModel.Title"/>, which closes with its ×. The window owns them from then on and disposes
    /// them when their tab closes.
    /// </summary>
    void Keep(FindResultsViewModel results);
}

/// <summary>A match's row: its line trimmed around it with the match picked out, its file and line at the right.</summary>
public sealed partial class FindHitRow : ObservableObject
{
    /// <summary>How much of the line before the match is shown, at most, once leading spaces are dropped.</summary>
    private const int MostBefore = 60;

    /// <summary>How much of the match and of what follows it are shown, at most.</summary>
    private const int MostShown = 200;

    public FindHitRow(FindHit hit)
    {
        Hit = hit;
        FileName = Path.GetFileName(hit.Path);
        var slash = hit.RelativePath.LastIndexOf('/');
        Folder = slash < 0 ? "" : hit.RelativePath[..slash];
        (Before, Match, After) = Parts(hit);
    }

    public FindHit Hit { get; }

    public string FileName { get; }

    /// <summary>The file's folder from the project's, with forward slashes; empty at the top.</summary>
    public string Folder { get; }

    public int Line => Hit.Line;

    /// <summary>The line up to the match, its spaces at the start dropped, and cut short from its start when long.</summary>
    public string Before { get; }

    public string Match { get; }

    public string After { get; }

    /// <summary>At the row's right: <c>Player.cpp 42</c>.</summary>
    public string Location => string.Create(CultureInfo.InvariantCulture, $"{FileName} {Hit.Line}");

    /// <summary>Its path from the project's folder and its line, for the tip.</summary>
    public string Tip => string.Create(CultureInfo.InvariantCulture, $"{Hit.RelativePath}:{Hit.Line}:{Hit.Column}");

    /// <summary>Left out of <b>Replace all</b> in the Find window.</summary>
    [ObservableProperty]
    public partial bool IsExcluded { get; set; }

    /// <summary>What a screen reader reads for its row.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Before}{Match}{After}, {FileName} line {Hit.Line}");

    internal static (string Before, string Match, string After) Parts(FindHit hit)
    {
        var text = hit.LineText.Replace('\t', ' ');
        var at = Math.Clamp(hit.MatchStart, 0, text.Length);
        var length = Math.Min(hit.Length, text.Length - at);
        var start = 0;
        while (start < at && text[start] == ' ')
        {
            start++;
        }
        var before = text[start..at];
        if (before.Length > MostBefore)
        {
            before = "…" + before[^(MostBefore - 20)..];
        }
        else if (hit.LineStart > 0)
        {
            before = "…" + before;
        }
        var match = length > MostShown ? text.Substring(at, MostShown) + "…" : text.Substring(at, length);
        var after = text[(at + length)..].TrimEnd();
        if (after.Length > MostShown)
        {
            after = after[..MostShown] + "…";
        }
        return (before, match, after);
    }
}

/// <summary>The selected match in its file, for the preview under the list.</summary>
/// <param name="Scope">The TextMate grammar that colors it, or null for plain text.</param>
public sealed record FindPreview(string Path, string RelativePath, string Text, string? Scope, int Line, int Column, int Length);

/// <summary>How a replace went: how many matches in how many files, and the files it couldn't change, with why.</summary>
public sealed record ReplaceOutcome(int Replaced, int Files, IReadOnlyList<string> Problems)
{
    public string Summary => Replaced == 0 && Problems.Count > 0
        ? "Nothing was replaced."
        : $"Replaced {FindInFilesSearch.Count(Replaced, "match", "matches")} in {FindInFilesSearch.Count(Files, "file", "files")}.";
}

/// <summary>
/// One project's Find in files, for the popup and the Find window (DESIGN.md §18, "Project files", "Finding in files"):
/// the files the composer's <c>@</c> lists, looked through off the UI thread with Core's <see cref="FindInFiles"/>, and
/// replacing, in the open files' editors or on disk.
/// </summary>
internal sealed class FindInFilesSearch(AppServices services, IFindInFilesHost host)
{
    /// <summary>The index searched, held while it's in use: the services hold indexes weakly.</summary>
    private ProjectFileIndex? _index;

    public AppServices Services => services;

    public IFindInFilesHost Host => host;

    /// <summary>The text each open file had when the latest search started: what the preview shows for it.</summary>
    public IReadOnlyDictionary<string, string> OpenTexts { get; private set; } = new Dictionary<string, string>();

    /// <summary>The files the latest search listed were cut short at the <c>@</c> list's most.</summary>
    public bool IsListCut { get; private set; }

    /// <summary>
    /// Looks for <paramref name="matcher"/>'s query as <paramref name="settings"/> say, handing each batch to
    /// <paramref name="onBatch"/> on the caller's thread (the UI thread). Returns the last batch, or null once
    /// <paramref name="cancellationToken"/> is cancelled, when nothing more is handed on.
    /// </summary>
    public async Task<FindBatch?> RunAsync(FindSettings settings, FindMatcher matcher, Action<FindBatch> onBatch, CancellationToken cancellationToken)
    {
        var (folder, under) = Where(settings);
        var index = _index is { } held && FindInFiles.PathComparer.Equals(held.Folder, folder) ? held : services.FileIndexFor(folder);
        _index = index;
        IReadOnlyList<IndexedPath> paths;
        try
        {
            paths = await index.GetAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            paths = [];
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        IsListCut = paths.Count(p => !p.IsFolder) >= index.MaxFiles;
        OpenTexts = host.OpenTexts();
        var request = new FindRequest(index.Folder, [.. paths.Select(p => p.Path)], matcher)
        {
            Under = under,
            Mask = FileMask.Parse(settings.FileMask),
            OpenTexts = OpenTexts,
        };
        FindBatch? last = null;
        try
        {
            await foreach (var batch in FindInFiles.SearchAsync(request, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
                onBatch(batch);
                last = batch;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        return last;
    }

    /// <summary>The folder whose files are listed, and the folder under it to look in, relative with a slash at its end.</summary>
    private static (string Folder, string Under) Where(FindSettings settings)
    {
        if (settings.Directory is not { Length: > 0 } directory)
        {
            return (settings.Project, "");
        }
        var relative = Path.GetRelativePath(settings.Project, directory);
        if (relative == ".")
        {
            return (settings.Project, "");
        }
        // A folder outside the project lists its own files.
        if (FolderListing.IsUp(relative) || Path.IsPathRooted(relative))
        {
            return (directory, "");
        }
        return (settings.Project, relative.Replace('\\', '/').TrimEnd('/') + "/");
    }

    /// <summary>
    /// Replaces as <paramref name="targets"/> say: in an open file's editor, which is left unsaved so its Undo takes it
    /// back, and otherwise on disk, through <see cref="AppServices.BeforeFilesWrite"/>. Each file is searched again first.
    /// </summary>
    public async Task<ReplaceOutcome> ReplaceAsync(FindMatcher matcher, string replacement, IReadOnlyList<ReplaceTarget> targets)
    {
        var results = new List<FileReplaceResult>();
        var onDisk = new List<ReplaceTarget>();
        foreach (var target in targets)
        {
            if (host.OpenDocument(target.Path) is { } document)
            {
                var plan = FindReplace.Plan(document.Text, target, matcher, replacement);
                if (plan.Problem is null)
                {
                    document.Replace(plan.Edits);
                }
                results.Add(new FileReplaceResult(target.Path, target.RelativePath, plan.Problem is null ? plan.Count : 0, plan.Problem));
            }
            else
            {
                onDisk.Add(target);
            }
        }
        results.AddRange(await FindReplace.OnDiskAsync(onDisk, matcher, replacement, services.BeforeFilesWrite));
        return new ReplaceOutcome(results.Sum(r => r.Replaced), results.Count(r => r.Replaced > 0),
            [.. results.Where(r => r.Problem is not null).Select(r => $"{Path.GetFileName(r.Path)}: {r.Problem}")]);
    }

    /// <summary>A file's text now: in its editor when it's open, else on disk; null when it isn't text to search.</summary>
    public async Task<string?> TextAsync(string path) =>
        host.OpenText(path) ?? await Task.Run(() => TextFile.Read(path) is { Problem: TextFileProblem.None } content ? content.Text : null);

    /// <summary>The selected match's file, for the preview: in its editor when it's open, else as it is on disk.</summary>
    public async Task<FindPreview?> PreviewAsync(FindHit hit)
    {
        if (await TextAsync(hit.Path) is not { } text)
        {
            return null;
        }
        var firstLine = text.AsSpan(0, Math.Min(text.Length, 200)).IndexOfAny('\r', '\n') is var end and >= 0 ? text[..end] : text[..Math.Min(text.Length, 200)];
        return new FindPreview(hit.Path, hit.RelativePath, text, EditorLanguages.For(hit.Path, firstLine).Scope, hit.Line, hit.Column, hit.Length);
    }

    /// <summary>The matches in one file now, all of them: after a replace in it.</summary>
    public async Task<IReadOnlyList<FindHit>> SearchFileAsync(string path, string relativePath, FindMatcher matcher)
    {
        if (await TextAsync(path) is not { } text)
        {
            return [];
        }
        try
        {
            return await Task.Run(() => FindInFiles.Search(path, relativePath, text, matcher, int.MaxValue, out _));
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return [];
        }
    }

    /// <summary>A count with its noun: <c>1 match</c>, <c>1,204 matches</c>.</summary>
    internal static string Count(int count, string one, string many) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? one : many)}");

    /// <summary>What a search's counts say: <i>57 matches in 12 files (first 1,000 shown)</i>, or <i>No matches</i>.</summary>
    internal static string Summary(FindBatch batch) => Summary(batch.Matches, batch.FilesWithMatches, batch.IsCapped, batch.IsComplete);

    /// <inheritdoc cref="Summary(FindBatch)"/>
    internal static string Summary(int matches, int files, bool capped, bool complete)
    {
        if (matches == 0)
        {
            return complete ? "No matches" : "Searching…";
        }
        var text = $"{Count(matches, "match", "matches")} in {Count(files, "file", "files")}";
        if (capped)
        {
            text += string.Create(CultureInfo.CurrentCulture, $" (first {FindInFiles.MostHits:N0} shown)");
        }
        if (!complete)
        {
            text += " · Searching…";
        }
        return text;
    }

    /// <summary>What's said after the counts: files skipped, and a list cut short.</summary>
    internal string Notes(FindBatch batch)
    {
        var notes = new List<string>();
        if (batch.TimedOut > 0)
        {
            notes.Add($"The regular expression took too long on {Count(batch.TimedOut, "file", "files")}, so {(batch.TimedOut == 1 ? "it was" : "they were")} skipped.");
        }
        if (IsListCut)
        {
            notes.Add(string.Create(CultureInfo.CurrentCulture, $"The project has more files than the {_index?.MaxFiles ?? 0:N0} searched."));
        }
        return string.Join(' ', notes);
    }
}
