using System.Collections.ObjectModel;
using System.Globalization;
using Claudette.Core.ProjectFiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>A file in the Find window's results, with its matches under it (DESIGN.md §18, "Finding in files").</summary>
public sealed partial class FindResultFile : ObservableObject
{
    public FindResultFile(string path, string relativePath, int count)
    {
        Path = path;
        RelativePath = relativePath;
        Count = count;
        var slash = relativePath.LastIndexOf('/');
        Folder = slash < 0 ? "" : relativePath[..slash];
    }

    public string Path { get; }

    public string RelativePath { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Its folder from the project's, muted beside its name; empty at the top.</summary>
    public string Folder { get; }

    /// <summary>How many matches it has, those past the most shown too.</summary>
    public int Count { get; }

    public string CountText => FindInFilesSearch.Count(Count, "match", "matches");

    public ObservableCollection<FindHitRow> Hits { get; } = [];

    /// <summary>Its matches show under it; a click on its row folds them away.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>What a screen reader reads for its row.</summary>
    public override string ToString() => $"{RelativePath}, {CountText}";
}

/// <summary>
/// One search's results kept in the Find window, a tab of their own docked at the bottom (DESIGN.md §18, "Finding in
/// files"): the matches by file, the query and its options over them, <b>Search again</b>, and in replace mode the
/// replacement and <b>Replace all</b>, which leaves out the matches unticked.
/// </summary>
public sealed partial class FindResultsViewModel : ObservableObject, IDisposable
{
    private readonly FindInFilesSearch _search;
    private readonly InlineConfirmation _replaceAll;
    private readonly List<FindFileMatches> _files = [];
    private CancellationTokenSource? _searching;
    private bool _disposed;

    internal FindResultsViewModel(FindInFilesSearch search, FindSettings settings, bool replaceMode, string replacement)
    {
        _search = search;
        Settings = settings;
        IsReplaceMode = replaceMode;
        Replacement = replacement;
        _replaceAll = new InlineConfirmation(ReplaceAllAsync);
        _replaceAll.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsAskingToReplaceAll));
    }

    /// <summary>What was looked for, and where.</summary>
    public FindSettings Settings { get; }

    /// <summary>Its tab's title: the query, cut short when long.</summary>
    public string Title => Settings.Query.Length > 30 ? $"{Settings.Query[..29]}…" : Settings.Query;

    /// <summary>The options it was looked for with, over the matches: <i>Match case · Words · *.cs · in Source</i>.</summary>
    public string OptionsText
    {
        get
        {
            var parts = new List<string>();
            if (Settings.MatchCase)
            {
                parts.Add("Match case");
            }
            if (Settings.WholeWords)
            {
                parts.Add("Words");
            }
            if (Settings.IsRegex)
            {
                parts.Add("Regex");
            }
            if (Settings.FileMask.Trim().Length > 0)
            {
                parts.Add(Settings.FileMask.Trim());
            }
            parts.Add(Settings.Directory is { } directory ? $"in {System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(directory))}" : "in the project");
            return string.Join(" · ", parts);
        }
    }

    public bool IsReplaceMode { get; }

    [ObservableProperty]
    public partial string Replacement { get; set; } = "";

    public ObservableCollection<FindResultFile> Files { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    public partial string Notes { get; private set; } = "";

    public bool HasNotes => Notes.Length > 0;

    [ObservableProperty]
    public partial bool IsSearching { get; private set; }

    public int MatchCount { get; private set; }

    public int FileCount { get; private set; }

    /// <summary>The popup's results, as they were when they were kept.</summary>
    internal void Load(IReadOnlyList<FindHit> hits, IReadOnlyList<FindFileMatches> files, int matches, int fileCount, string summary, string notes)
    {
        Files.Clear();
        _files.Clear();
        _files.AddRange(files);
        var counts = files.ToDictionary(f => f.Path, f => f, FindInFiles.PathComparer);
        foreach (var group in hits.GroupBy(h => h.Path, FindInFiles.PathComparer))
        {
            var first = group.First();
            var file = new FindResultFile(first.Path, first.RelativePath, counts.TryGetValue(first.Path, out var known) ? known.Count : group.Count());
            foreach (var hit in group)
            {
                file.Hits.Add(new FindHitRow(hit));
            }
            Files.Add(file);
        }
        MatchCount = matches;
        FileCount = fileCount;
        Summary = summary;
        Notes = notes;
        OnPropertyChanged(nameof(ReplaceAllText));
    }

    /// <summary><b>Search again</b>: the same search, now, in place of what's shown.</summary>
    [RelayCommand]
    private async Task SearchAgainAsync()
    {
        if (_disposed || !FindMatcher.TryCreate(Settings.ToQuery(), out var matcher, out _))
        {
            return;
        }
        _searching?.Cancel();
        _searching?.Dispose();
        var cancel = _searching = new CancellationTokenSource();
        _replaceAll.Cancel();
        IsSearching = true;
        // Each batch brings its own matches and files; the counts are the whole search's so far.
        var hits = new List<FindHit>();
        var files = new List<FindFileMatches>();
        var last = await _search.RunAsync(Settings, matcher, batch =>
        {
            if (ReferenceEquals(cancel, _searching))
            {
                hits.AddRange(batch.Hits);
                files.AddRange(batch.Files);
            }
        }, cancel.Token);
        // A later search took its place, or the window closed.
        if (last is null || !ReferenceEquals(cancel, _searching))
        {
            return;
        }
        IsSearching = false;
        Load(hits, files, last.Matches, last.FilesWithMatches, FindInFilesSearch.Summary(last), _search.Notes(last));
    }

    /// <summary>A click on a match, or Enter: it opens in the project's Files tab, selected.</summary>
    [RelayCommand]
    private Task OpenAsync(FindHitRow? row) => row is null ? Task.CompletedTask : _search.Host.OpenHitAsync(row.Hit);

    /// <summary>A file's row: its matches fold away, or show again.</summary>
    [RelayCommand]
    private static void ToggleFile(FindResultFile? file)
    {
        if (file is not null)
        {
            file.IsExpanded = !file.IsExpanded;
        }
    }

    // ---- Replacing ---------------------------------------------------------------------------------------------------

    public string ReplaceAllText
    {
        get
        {
            var excluded = Files.Sum(f => f.Hits.Count(h => h.IsExcluded));
            var count = MatchCount - excluded;
            var text = $"Replace {FindInFilesSearch.Count(count, "match", "matches")} in {FindInFilesSearch.Count(FileCount, "file", "files")}?";
            return excluded == 0 ? text : string.Create(CultureInfo.CurrentCulture, $"{text} {excluded:N0} left out.");
        }
    }

    public bool IsAskingToReplaceAll => _replaceAll.IsOpen;

    public IRelayCommand ConfirmReplaceAllCommand => _replaceAll.ConfirmCommand;

    public IRelayCommand CancelReplaceAllCommand => _replaceAll.CancelCommand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReplaceNotice))]
    public partial string? ReplaceNotice { get; private set; }

    public bool HasReplaceNotice => ReplaceNotice is not null;

    public ObservableCollection<string> ReplaceProblems { get; } = [];

    /// <summary><b>Replace all</b>: asks first, in place, saying how many and how many are left out.</summary>
    [RelayCommand]
    private void AskReplaceAll()
    {
        if (IsReplaceMode && MatchCount > 0 && !IsSearching)
        {
            OnPropertyChanged(nameof(ReplaceAllText));
            _replaceAll.Ask();
        }
    }

    /// <summary>Every match the search found but those left out, each file searched again first; then the search runs again.</summary>
    private async Task ReplaceAllAsync()
    {
        if (!FindMatcher.TryCreate(Settings.ToQuery(), out var matcher, out _) || _files.Count == 0)
        {
            return;
        }
        var excluded = Files.SelectMany(f => f.Hits).Where(h => h.IsExcluded).Select(h => h.Hit).ToLookup(h => h.Path, FindInFiles.PathComparer);
        var outcome = await _search.ReplaceAsync(Settings.Project, matcher, Replacement, [.. _files.Select(f => new ReplaceTarget(f.Path, f.RelativePath) { Excluded = [.. excluded[f.Path]] })]);
        ReplaceProblems.Clear();
        foreach (var problem in outcome.Problems)
        {
            ReplaceProblems.Add(problem);
        }
        ReplaceNotice = outcome.Summary;
        await SearchAgainAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _searching?.Cancel();
        _searching?.Dispose();
        _searching = null;
    }
}
