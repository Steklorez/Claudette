using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Diffs;
using Deventry.Core.Processes;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary><b>Clone a repository…</b> (DESIGN.md §4, "Opening a tab"): the dialog's checks, and the clone opening as a tab.</summary>
public class CloneRepositoryTests
{
    private static readonly bool GitInstalled = FileProbe.Instance.FindOnPath(OperatingSystem.IsWindows() ? "git.exe" : "git") is not null;

    private static CloneRepositoryViewModel Open(TabTestHarness h)
    {
        h.Shell.OpenCloneCommand.Execute(null);
        return Assert.IsType<CloneRepositoryViewModel>(h.Shell.Clone);
    }

    [Fact]
    public async Task The_picker_hands_over_to_the_dialog_which_starts_beside_the_last_folder_opened()
    {
        await using var h = new TabTestHarness();
        var projects = Directory.CreateDirectory(Path.Combine(h.Root, "projects")).FullName;
        FolderHistory.Touch(h.Services.State, Path.Combine(projects, "api"), h.Time.GetUtcNow(), 10);
        h.Shell.NewTabCommand.Execute(null);
        var picker = Assert.IsType<NewTabPickerViewModel>(h.Shell.Picker);

        picker.OpenCloneCommand.Execute(null);

        Assert.Null(h.Shell.Picker);
        var clone = Assert.IsType<CloneRepositoryViewModel>(h.Shell.Clone);
        Assert.Equal(projects, clone.Parent);
        Assert.Equal("", clone.Url);
        Assert.False(clone.HasProblem);
        Assert.False(clone.CanClone);
    }

    [Fact]
    public async Task The_name_follows_the_address_until_it_is_typed()
    {
        await using var h = new TabTestHarness();
        var clone = Open(h);

        clone.Url = "https://github.com/org/repo.git";
        Assert.Equal("repo", clone.Name);
        Assert.True(clone.CanClone);
        Assert.Equal(Path.Combine(clone.Parent, "repo"), clone.Target);

        clone.Url = "git@github.com:org/tools.git";
        Assert.Equal("tools", clone.Name);

        clone.Name = "mine";
        clone.Url = "https://github.com/org/other";
        Assert.Equal("mine", clone.Name);

        // Cleared, it follows the address again.
        clone.Name = "";
        Assert.Equal("other", clone.Name);
    }

    [Fact]
    public async Task A_problem_is_shown_and_keeps_the_clone_from_starting()
    {
        await using var h = new TabTestHarness();
        var clone = Open(h);
        clone.Url = "https://github.com/org/repo.git";
        clone.Parent = h.Root;
        Directory.CreateDirectory(Path.Combine(h.Root, "repo"));

        Assert.Equal("There's already a folder named repo there.", clone.Problem);
        Assert.False(clone.CanClone);
        Assert.False(clone.CloneCommand.CanExecute(null));

        clone.Name = "a/b";
        Assert.Equal("The name must be a single folder name, without slashes.", clone.Problem);
        clone.Name = "fresh";
        Assert.Null(clone.Problem);
        Assert.True(clone.CanClone);
    }

    [Fact]
    public async Task Browse_sets_the_folder_to_clone_into_and_cancel_closes()
    {
        await using var h = new TabTestHarness();
        var clone = Open(h);
        h.Platform.FolderToPick = h.WorkFolder;

        await clone.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(h.WorkFolder, clone.Parent);

        clone.CancelCommand.Execute(null);
        Assert.Null(h.Shell.Clone);
    }

    [Fact]
    public async Task A_clone_opens_as_a_new_tab()
    {
        Assert.SkipWhen(!GitInstalled, "git isn't on PATH.");
        await using var h = new TabTestHarness();
        var source = await MakeBareRepositoryAsync(h);
        var projects = Directory.CreateDirectory(Path.Combine(h.Root, "projects")).FullName;
        var clone = Open(h);
        clone.Url = source;
        clone.Parent = projects;
        Assert.Equal("source", clone.Name);

        await clone.CloneCommand.ExecuteAsync(null);

        Assert.Null(clone.Error);
        Assert.Null(h.Shell.Clone);
        var expected = Path.Combine(projects, "source");
        Assert.True(Directory.Exists(Path.Combine(expected, ".git")));
        Assert.Equal(expected, h.Shell.SelectedTab?.Folder);
        Assert.Equal(expected, h.Services.State.RecentFolders[0].Path);
    }

    [Fact]
    public async Task A_failed_clone_stays_open_with_what_git_said()
    {
        Assert.SkipWhen(!GitInstalled, "git isn't on PATH.");
        await using var h = new TabTestHarness();
        var projects = Directory.CreateDirectory(Path.Combine(h.Root, "projects")).FullName;
        var clone = Open(h);
        clone.Url = Path.Combine(h.Root, "no-such-repo.git");
        clone.Parent = projects;

        await clone.CloneCommand.ExecuteAsync(null);

        Assert.NotNull(h.Shell.Clone);
        Assert.False(clone.IsCloning);
        Assert.Contains("repository", Assert.IsType<string>(clone.Error), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(h.Shell.AllTabs);
        // The next change clears it.
        clone.Url = "https://example.invalid/org/repo.git";
        Assert.Null(clone.Error);
    }

    /// <summary>A repository with one commit, as a bare clone under the harness's folder, which git clones over the file system.</summary>
    private static async Task<string> MakeBareRepositoryAsync(TabTestHarness h)
    {
        var work = Directory.CreateDirectory(Path.Combine(h.Root, "upstream")).FullName;
        await GitAsync(work, "init", "-q");
        await File.WriteAllTextAsync(Path.Combine(work, "README.md"), "hello\n", TestContext.Current.CancellationToken);
        await GitAsync(work, "add", "README.md");
        await GitAsync(work, "-c", "user.email=t@example.com", "-c", "user.name=t", "commit", "-q", "-m", "init");
        var bare = Path.Combine(h.Root, "source.git");
        await GitAsync(h.Root, "clone", "-q", "--bare", work, bare);
        return bare;
    }

    private static async Task GitAsync(string folder, params string[] arguments)
    {
        var result = await ProcessRunner.RunAsync(new ProcessLauncher(), new ProcessStartSpec("git", arguments) { WorkingDirectory = folder },
            Waiting.Timeout, TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {result.StandardError}");
    }
}
