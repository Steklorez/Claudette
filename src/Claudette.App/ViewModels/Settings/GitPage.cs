using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.Settings;

/// <summary>A choice for <b>Remove worktrees no tab has used for</b>.</summary>
public sealed record InactiveDaysChoice(int Days)
{
    public override string ToString() => Days == 0 ? "Never" : $"{Days} days";
}

/// <summary>
/// Settings → Source control → Git (DESIGN.md §14): the branch on each tab's row, cleaning up worktrees, the git
/// Claudette runs, and what the Project Files tab shows of git. <b>Show the git branch</b> and <b>Worktrees</b> keep their
/// keys from Conversation and General, where they were before, so the choices made there carry over.
/// </summary>
public sealed partial class GitPage : SettingsPage, IDisposable
{
    private readonly CancellationTokenSource _closing = new();

    public GitPage(SettingsContext context) : base(context, SettingsCategory.Git)
    {
        Section("tabRows", "Tab rows",
        [
            Setting("appearance.showBranchOnTabs", "Show the git branch", s => s.Appearance.ShowBranchOnTabs, (s, v) => s.Appearance.ShowBranchOnTabs = v,
                "The branch each tab's folder has checked out, such as “main”.",
                "At the right of the row's second line. A tab in a worktree shows the worktree's name instead.", ["git", "sidebar", "worktree", "branch"]),
        ], description: "What each tab's row in the sidebar shows of git. The rest is on Conversation.");
        Section("worktrees", "Worktrees",
        [
            Setting("general.removeMergedWorktrees", "Remove worktrees once everything in them is merged",
                s => s.General.RemoveMergedWorktrees, (s, v) => s.General.RemoveMergedWorktrees = v,
                "Every commit is on another branch, so nothing is lost.",
                "The worktree and its branch go a day after a tab last used it. One with uncommitted changes is always kept, and files git ignores there, such as build output, go with it.",
                ["clean up", "git"]),
            Setting("general.removeInactiveWorktreesAfterDays", "Remove worktrees no tab has used for",
                s => s.General.RemoveInactiveWorktreesAfterDays, (s, v) => s.General.RemoveInactiveWorktreesAfterDays = v,
                "Its branch stays when it has commits on no other branch.",
                "So a new worktree tab can carry on from the branch. One with uncommitted changes is always kept.",
                ["clean up", "stale", "git"]),
            Info("general.cleanUpWorktrees", "Last cleanup", keywords: ["clean up now", "worktrees"]),
        ], description: "The worktrees of worktree tabs that no open tab works in.");
        Section("git", "Git",
        [
            Setting("git.path", "Path to git", s => s.Git.Path, (s, v) => s.Git.Path = v,
                "Empty uses the first git on your PATH, as a terminal does.",
                "Claudette runs it for Changed files, worktrees, the @ list, what the Files tab dims and a source build's new commits, and for the git difftool. Claude's own git commands, in its Bash tool, run the git Claude Code finds, whatever this says.",
                ["git.exe", "executable", "binary", "location", "version"]),
        ], description: "The git Claudette runs to ask about your repositories.");
        Section("projectFiles", "Project Files tab",
        [
            Setting("git.newFilesTabsShow", "New Files tabs show", s => s.Git.NewFilesTabsShow, (s, v) => s.Git.NewFilesTabsShow = v,
                "What a Files tab marks as changed when it first opens. Each changes its own with its explorer's ⋯ menu.",
                "Changes against git HEAD also shows what you, commands and closed tabs changed. Outside a git repository, a Files tab shows Claude's changes.",
                ["against git head", "claude's changes", "explorer", "files tab", "diff"]),
            Setting("git.ignoredFiles", "Files git ignores", s => s.Git.IgnoredFiles, (s, v) => s.Git.IgnoredFiles = v,
                "How the explorer shows them. A change applies to open Files tabs at once.",
                "Outside a git repository, the folders the @ list skips, such as node_modules, bin and obj.",
                ["gitignore", ".gitignore", "ignored", "explorer", "hide", "dim", "node_modules"]),
        ], description: "What each folder group's tab of its project's files shows of git.");
    }

    public override string IconKey => "IconBranch";

    public override string Description => "The git branch on tab rows, worktrees, the git Claudette runs, and git in the Files tab.";

    /// <summary>The window closed: a check still running is dropped. The window's view model is disposed more than once.</summary>
    public void Dispose()
    {
        if (!_closing.IsCancellationRequested)
        {
            _closing.Cancel();
        }
    }

    // ---- Tab rows (DESIGN.md §4, "Sidebar") ---------------------------------------------------------------------------

    /// <summary>The git branch, or worktree, on each tab's row (DESIGN.md §4, "Sidebar").</summary>
    public bool ShowBranchOnTabs
    {
        get => Settings.Appearance.ShowBranchOnTabs;
        set => Set(value, v => Settings.Appearance.ShowBranchOnTabs = v);
    }

    // ---- Cleaning up worktrees (DESIGN.md §4, "Cleaning up worktrees") ---------------------------------------------

    public bool RemoveMergedWorktrees
    {
        get => Settings.General.RemoveMergedWorktrees;
        set
        {
            Set(value, v => Settings.General.RemoveMergedWorktrees = v);
            OnPropertyChanged(nameof(CanCleanUpWorktrees));
        }
    }

    /// <summary>The choices for <b>Remove worktrees no tab has used for</b>; 0 is never.</summary>
    public IReadOnlyList<InactiveDaysChoice> InactiveDaysChoices { get; } = [.. new[] { 0, 7, 14, 30, 90 }.Select(d => new InactiveDaysChoice(d))];

    public InactiveDaysChoice RemoveInactiveWorktreesAfter
    {
        get => InactiveDaysChoices.FirstOrDefault(c => c.Days == Settings.General.RemoveInactiveWorktreesAfterDays)
            ?? new InactiveDaysChoice(Settings.General.RemoveInactiveWorktreesAfterDays);
        set
        {
            if (value is not null && value.Days != Settings.General.RemoveInactiveWorktreesAfterDays)
            {
                Set(value.Days, v => Settings.General.RemoveInactiveWorktreesAfterDays = v);
                OnPropertyChanged(nameof(CanCleanUpWorktrees));
            }
        }
    }

    /// <summary>What the last cleanup removed, and when: "Last removed brisk-otter and calm-heron, 2 h ago."</summary>
    public string WorktreeCleanupText
    {
        get
        {
            if (_cleanupResult is { } result)
            {
                return result;
            }
            if (Services.State.LastWorktreeCleanup is not { Removed.Count: > 0 } last)
            {
                return "Nothing removed yet. Claudette looks a minute after it starts, then every hour.";
            }
            var names = last.Removed.Select(p => Path.GetFileName(Path.TrimEndingDirectorySeparator(p))).ToList();
            return $"Last removed {Core.Formats.List(names)}, {Core.Formats.Ago(Services.Time.GetUtcNow() - last.At)}.";
        }
    }

    public bool CanCleanUpWorktrees => (RemoveMergedWorktrees || Settings.General.RemoveInactiveWorktreesAfterDays > 0) && !_cleaningUp;

    private string? _cleanupResult;
    private bool _cleaningUp;

    /// <summary><b>Clean up now</b>: a pass straight away, rather than at the next hour.</summary>
    [RelayCommand]
    private async Task CleanUpWorktreesAsync()
    {
        _cleaningUp = true;
        OnPropertyChanged(nameof(CanCleanUpWorktrees));
        try
        {
            var removed = await Services.Worktrees.RunAsync();
            _cleanupResult = removed.Count switch
            {
                0 => "Nothing to remove right now.",
                1 => $"Removed {Path.GetFileName(removed[0])}.",
                _ => $"Removed {removed.Count} worktrees: {Core.Formats.List(removed.Select(p => Path.GetFileName(Path.TrimEndingDirectorySeparator(p))).ToList())}.",
            };
        }
        finally
        {
            _cleaningUp = false;
            OnPropertyChanged(nameof(CanCleanUpWorktrees));
            OnPropertyChanged(nameof(WorktreeCleanupText));
        }
    }

    // ---- The git Claudette runs (DESIGN.md §14, "Path to git") -------------------------------------------------------

    /// <summary>Empty for the first git on the user environment's PATH. Applies to the next git Claudette runs.</summary>
    public string GitPath
    {
        get => Settings.Git.Path ?? "";
        set
        {
            var path = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (path == Settings.Git.Path)
            {
                return;
            }
            Set(path, v => Settings.Git.Path = v);
            CheckGit();
        }
    }

    [RelayCommand]
    private async Task BrowseGitPathAsync()
    {
        if (await Services.Platform.PickFileAsync("Choose the git program") is { } path)
        {
            GitPath = path;
            OnPropertyChanged(nameof(GitPath));
        }
    }

    /// <summary>The git found and its version, under the box: "git 2.47.1 at C:\Program Files\Git\cmd\git.exe". Null until it's checked, and with a problem.</summary>
    [ObservableProperty]
    public partial string? GitFoundText { get; private set; }

    /// <summary>Why the git set can't be used: there's nothing at the path, or it isn't git. Null while it can.</summary>
    [ObservableProperty]
    public partial string? GitProblem { get; private set; }

    /// <summary>The latest check of the git set, for tests to wait on.</summary>
    internal Task Checking { get; private set; } = Task.CompletedTask;

    /// <summary>Checked each time the page shows, since git can be installed, moved or updated while Claudette runs.</summary>
    public override void OnShown() => CheckGit();

    /// <summary>Resetting the section puts the path back: the git found then is checked.</summary>
    protected override void OnReset() => CheckGit();

    private void CheckGit() => Checking = CheckGitAsync();

    /// <summary>Runs <c>git --version</c>, which a slow disk or a program that isn't git can hold up: not on the UI thread.</summary>
    private async Task CheckGitAsync()
    {
        var path = Settings.Git.Path;
        var git = Services.Git;
        var token = _closing.Token;
        Core.Git.GitCheck check;
        try
        {
            check = await Task.Run(() => git.CheckAsync(token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        // The path changed meanwhile: its own check follows.
        if (path != Settings.Git.Path)
        {
            return;
        }
        GitFoundText = check.Problem is null ? $"git {check.Version} at {check.Path}" : null;
        GitProblem = check.Problem;
    }

    // ---- The Project Files tab (DESIGN.md §18, "Project files") -------------------------------------------------------

    /// <summary><b>New Files tabs show</b> Claude's changes, against what each file held before Claude first changed it.</summary>
    public bool NewFilesTabsShowClaudesChanges
    {
        get => Settings.Git.NewFilesTabsShow == FilesTabChanges.ClaudesChanges;
        set
        {
            if (value)
            {
                SetNewFilesTabsShow(FilesTabChanges.ClaudesChanges);
            }
        }
    }

    /// <summary><b>New Files tabs show</b> changes against git's HEAD.</summary>
    public bool NewFilesTabsShowGitHead
    {
        get => Settings.Git.NewFilesTabsShow == FilesTabChanges.GitHead;
        set
        {
            if (value)
            {
                SetNewFilesTabsShow(FilesTabChanges.GitHead);
            }
        }
    }

    private void SetNewFilesTabsShow(FilesTabChanges changes)
    {
        if (Settings.Git.NewFilesTabsShow == changes)
        {
            return;
        }
        Settings.Git.NewFilesTabsShow = changes;
        OnPropertyChanged(nameof(NewFilesTabsShowClaudesChanges));
        OnPropertyChanged(nameof(NewFilesTabsShowGitHead));
        Save();
    }

    /// <summary><b>Files git ignores</b> are dimmed in the explorer.</summary>
    public bool IgnoredFilesDimmed
    {
        get => Settings.Git.IgnoredFiles == IgnoredFiles.Dimmed;
        set
        {
            if (value)
            {
                SetIgnoredFiles(IgnoredFiles.Dimmed);
            }
        }
    }

    /// <summary><b>Files git ignores</b> are left out of the explorer.</summary>
    public bool IgnoredFilesHidden
    {
        get => Settings.Git.IgnoredFiles == IgnoredFiles.Hidden;
        set
        {
            if (value)
            {
                SetIgnoredFiles(IgnoredFiles.Hidden);
            }
        }
    }

    private void SetIgnoredFiles(IgnoredFiles files)
    {
        if (Settings.Git.IgnoredFiles == files)
        {
            return;
        }
        Settings.Git.IgnoredFiles = files;
        OnPropertyChanged(nameof(IgnoredFilesDimmed));
        OnPropertyChanged(nameof(IgnoredFilesHidden));
        Save();
    }
}
