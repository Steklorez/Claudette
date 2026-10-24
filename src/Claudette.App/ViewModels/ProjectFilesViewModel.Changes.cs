using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Claudette.Core.Git;
using Claudette.Core.ProjectFiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// Claude's changes in the Project Files tab (DESIGN.md §18, "Project files", "Claude's changes"): the project's open
/// tabs' changes, merged, or git's HEAD; the explorer's letters and <b>Show only changed files</b>; what each open file is
/// compared with; and <b>Reviewed</b>.
/// </summary>
public sealed partial class ProjectFilesViewModel
{
    /// <summary>How long after a tab's change the marks follow, so a turn's edits count once.</summary>
    internal static readonly TimeSpan ChangesDelay = TimeSpan.FromMilliseconds(200);

    private readonly UiTimeout _changesWait;
    private IReadOnlyDictionary<string, ProjectFileChange> _changes = new Dictionary<string, ProjectFileChange>();

    /// <summary>With git's HEAD: each changed file's letter, by full path.</summary>
    private IReadOnlyDictionary<string, GitChangeKind> _gitChanges = new Dictionary<string, GitChangeKind>();

    private readonly List<TabViewModel> _watchedTabs = [];

    /// <summary>The changes are against git's HEAD, as Changed files' switch does, rather than Claude's "before".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AgainstClaudeChanges))]
    public partial bool AgainstGitHead { get; set; }

    public bool AgainstClaudeChanges
    {
        get => !AgainstGitHead;
        set => AgainstGitHead = !value;
    }

    partial void OnAgainstGitHeadChanged(bool value)
    {
        UpdateChanges();
        OnFileStateChanged();
    }

    /// <summary>The explorer lists only the files changed, and the ones Claude deleted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTree), nameof(ShowsChangedList))]
    public partial bool ShowOnlyChanged { get; set; }

    partial void OnShowOnlyChangedChanged(bool value)
    {
        if (value)
        {
            BuildChangedRows();
        }
        OnFileStateChanged();
    }

    /// <summary>The tree shows: neither the found files' list nor the changed files'.</summary>
    public bool ShowsTree => !IsListingFound && !ShowOnlyChanged;

    public bool ShowsChangedList => !IsListingFound && ShowOnlyChanged;

    /// <summary>How paths compare on this OS: ignoring case on Windows and macOS.</summary>
    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>The files changed, for <b>Show only changed files</b>, by their paths from the project's folder.</summary>
    public ObservableCollection<ExplorerRow> ChangedRows { get; } = [];

    public bool HasNoChanges => ChangedRows.Count == 0;

    // ---- Following the group's tabs ------------------------------------------------------------------------------

    private void WatchGroupTabs()
    {
        Group.Tabs.CollectionChanged += OnGroupTabsChanged;
        foreach (var tab in Group.Tabs)
        {
            WatchTab(tab);
        }
    }

    private void StopWatchingGroupTabs()
    {
        Group.Tabs.CollectionChanged -= OnGroupTabsChanged;
        foreach (var tab in _watchedTabs.ToArray())
        {
            UnwatchTab(tab);
        }
    }

    private void OnGroupTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var tab in _watchedTabs.Except(Group.Tabs).ToArray())
        {
            UnwatchTab(tab);
        }
        foreach (var tab in Group.Tabs.Except(_watchedTabs).ToArray())
        {
            WatchTab(tab);
        }
        QueueChanges();
    }

    private void WatchTab(TabViewModel tab)
    {
        _watchedTabs.Add(tab);
        tab.ChangedFiles.ClaudeChangesUpdated += QueueChanges;
        tab.ChangedFiles.ReviewedChanged += OnReviewedChanged;
    }

    private void UnwatchTab(TabViewModel tab)
    {
        _watchedTabs.Remove(tab);
        tab.ChangedFiles.ClaudeChangesUpdated -= QueueChanges;
        tab.ChangedFiles.ReviewedChanged -= OnReviewedChanged;
    }

    private void QueueChanges() => _changesWait.Restart(ChangesDelay, UpdateChanges);

    /// <summary>
    /// A turn of one of the project's tabs ended, or a tool call changed a file: the open files are looked at on disk, so
    /// Claude's edits show at once even when the OS's notice is late.
    /// </summary>
    internal void OnTabWroteFiles() => _ = CheckOpenFilesOnDiskAsync();

    // ---- Working the changes out -------------------------------------------------------------------------------------

    /// <summary>The changes again: the explorer's letters, the changed files' list, and every open file's marks.</summary>
    internal void UpdateChanges() => _ = UpdateChangesAsync();

    private async Task UpdateChangesAsync()
    {
        if (_disposed)
        {
            return;
        }
        _changes = ProjectChanges.Merge(Group.Tabs.Select(t => new TabChanges(t.Id, t.DisplayName, t.ChangedFiles.ClaudeChanges)));
        if (AgainstGitHead && _inRepository)
        {
            var changes = await _services.Git.GetChangesAsync(Folder);
            _gitChanges = changes.ToDictionary(c => c.Path, c => c.Kind, PathComparer);
        }
        UpdateRowChanges();
        if (ShowOnlyChanged)
        {
            BuildChangedRows();
        }
        foreach (var file in OpenFiles)
        {
            ApplyBaseline(file);
        }
    }

    /// <summary>What <paramref name="file"/> is compared with now, and who changed it.</summary>
    private void ApplyBaseline(ProjectFileViewModel file) => _ = ApplyBaselineAsync(file);

    private async Task ApplyBaselineAsync(ProjectFileViewModel file)
    {
        var change = _changes.GetValueOrDefault(file.Path);
        file.IsReviewed = IsReviewed(file.Path);
        if (AgainstGitHead && _inRepository)
        {
            var head = await _services.Git.GetHeadContentAsync(Folder, file.Path);
            // A file git doesn't have yet is all new.
            file.SetBaseline(true, Core.Diffs.Revert.WithLineEndingsOf(head, file.Document?.Text), "compared with HEAD", change);
            return;
        }
        if (change is null)
        {
            file.SetBaseline(false, null, "", null);
            return;
        }
        file.SetBaseline(change.BeforeKnown, change.Before,
            change.IsNew ? "new since Claude created it" : "compared with before Claude's first change", change);
    }

    /// <summary>The letter a file has in the explorer, and whether its change is reviewed.</summary>
    private void ApplyChange(ExplorerRow row)
    {
        if (row.IsFolder)
        {
            row.ChangeLetter = null;
            var prefix = row.FullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            row.HasChangesInside = ChangedPaths().Any(p => p.StartsWith(prefix, PathComparison));
            return;
        }
        if (AgainstGitHead && _inRepository)
        {
            row.ChangeLetter = _gitChanges.TryGetValue(row.FullPath, out var kind) ? Letter(kind) : null;
            row.IsReviewed = false;
            return;
        }
        if (_changes.TryGetValue(row.FullPath, out var change))
        {
            row.ChangeLetter = change.IsNew ? "A" : "M";
            row.IsReviewed = IsReviewed(row.FullPath);
        }
        else
        {
            row.ChangeLetter = null;
            row.IsReviewed = false;
        }
    }

    private IEnumerable<string> ChangedPaths() => AgainstGitHead && _inRepository ? _gitChanges.Keys : _changes.Keys;

    private static string Letter(GitChangeKind kind) => kind switch
    {
        // An untracked file counts as added, as in Changed files (DESIGN.md §8).
        GitChangeKind.Added or GitChangeKind.Untracked => "A",
        GitChangeKind.Deleted => "D",
        GitChangeKind.Renamed => "R",
        _ => "M",
    };

    private void UpdateRowChanges()
    {
        foreach (var row in Rows)
        {
            ApplyChange(row);
        }
    }

    /// <summary>
    /// <b>Show only changed files</b>: each changed file with its folder beside it, by path; one that's gone from disk is
    /// struck through.
    /// </summary>
    private void BuildChangedRows()
    {
        ChangedRows.Clear();
        foreach (var path in ChangedPaths().OrderBy(p => FolderListing.Relative(Folder, p), StringComparer.OrdinalIgnoreCase))
        {
            var relative = FolderListing.Relative(Folder, path);
            var folder = Path.IsPathRooted(relative) ? Path.GetDirectoryName(path) ?? "" : relative.Contains('/') ? relative[..relative.LastIndexOf('/')].Replace('/', Path.DirectorySeparatorChar) : "";
            var row = new ExplorerRow(new TreeEntry(Path.GetFileName(path), path, relative, IsFolder: false), 0, folder)
            {
                IsDeleted = !File.Exists(path),
            };
            ApplyChange(row);
            ChangedRows.Add(row);
        }
        OnPropertyChanged(nameof(HasNoChanges));
    }

    // ---- Reviewed (DESIGN.md §8, "Reviewed") -----------------------------------------------------------------------

    /// <summary>Every one of the project's open tabs that changed the file has it ticked, and one did.</summary>
    internal bool IsReviewed(string path)
    {
        var changed = Group.Tabs.Where(t => t.ChangedFiles.HasClaudeChange(path)).ToList();
        return changed.Count > 0 && changed.All(t => t.ChangedFiles.IsFileReviewed(path));
    }

    /// <summary>
    /// <b>Reviewed</b> on a file: ticks it in every one of the project's open tabs whose Changed files lists it, at its
    /// latest change, as ticking it there does; the rows' reviewed icon follows.
    /// </summary>
    internal void SetReviewed(ProjectFileViewModel file, bool reviewed)
    {
        foreach (var tab in Group.Tabs.Where(t => t.ChangedFiles.HasClaudeChange(file.Path)).ToArray())
        {
            tab.ChangedFiles.SetFileReviewedFromProject(file.Path, reviewed);
        }
        OnReviewedChanged();
    }

    private void OnReviewedChanged()
    {
        foreach (var file in OpenFiles)
        {
            file.IsReviewed = IsReviewed(file.Path);
        }
        UpdateRowChanges();
        foreach (var row in ChangedRows)
        {
            ApplyChange(row);
        }
    }

    [RelayCommand]
    private void ToggleShowOnlyChanged() => ShowOnlyChanged = !ShowOnlyChanged;

    /// <summary>The peek's header: the tab that made the change, beside this one, scrolled to its tool call.</summary>
    internal void ShowChangeIn(ChangeAuthor author)
    {
        if (Group.Tabs.FirstOrDefault(t => t.Id == author.TabId) is not { } tab)
        {
            return;
        }
        _shell.ShowFromFiles(tab);
        if (author.LatestChange is { } toolUseId)
        {
            tab.ShowToolUse(toolUseId);
        }
    }
}
