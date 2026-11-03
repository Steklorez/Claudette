using Deventry.Core.Git;
using Deventry.Core.Tests.Support;

namespace Deventry.Core.Tests.Git;

/// <summary>
/// <b>Clone a repository…</b> (DESIGN.md §4, "Opening a tab"), against the real <c>git</c> and a bare repository on
/// disk, never the network.
/// </summary>
public sealed class GitCloneTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deventry-clone-{Guid.NewGuid():N}");
    private readonly IsolatedGitLauncher _launcher;
    private readonly GitClone _clone;

    public GitCloneTests()
    {
        Directory.CreateDirectory(_root);
        _launcher = new IsolatedGitLauncher(_root);
        _clone = new GitClone(new GitWorkingTree(_launcher, TimeProvider.System));
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(_root, recursive: true);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://github.com/org/repo.git", "repo")]
    [InlineData("https://github.com/org/repo", "repo")]
    [InlineData("https://github.com/org/repo/", "repo")]
    [InlineData("git@github.com:org/repo.git", "repo")]
    [InlineData("ssh://git@host:22/team/tools.git", "tools")]
    [InlineData(@"C:\src\bare.git", "bare")]
    [InlineData("/srv/git/project.git/", "project")]
    [InlineData("  ", "")]
    [InlineData("https://github.com/org/.git", ".git")]
    public void The_default_name_is_the_last_segment_without_dot_git(string url, string name) =>
        Assert.Equal(name, GitClone.DefaultName(url));

    [Fact]
    public void A_problem_names_what_is_missing_or_wrong()
    {
        Directory.CreateDirectory(Path.Combine(_root, "taken"));

        Assert.Equal("Enter the repository's address.", GitClone.Problem("", _root, "repo"));
        Assert.Equal("That folder doesn't exist.", GitClone.Problem("https://x/r.git", Path.Combine(_root, "nowhere"), "repo"));
        Assert.Equal("Enter a name for the clone's folder.", GitClone.Problem("https://x/r.git", _root, " "));
        Assert.Equal("The name must be a single folder name, without slashes.", GitClone.Problem("https://x/r.git", _root, "a/b"));
        Assert.Equal("The name must be a single folder name, without slashes.", GitClone.Problem("https://x/r.git", _root, ".."));
        Assert.Equal("There's already a folder named taken there.", GitClone.Problem("https://x/r.git", _root, "taken"));
        Assert.Null(GitClone.Problem("https://x/r.git", _root, "repo"));
    }

    [Fact]
    public async Task A_bare_repository_on_disk_is_cloned_into_the_named_folder()
    {
        Assert.SkipWhen(!IsolatedGitLauncher.GitInstalled, "git isn't on PATH.");
        var source = await MakeBareRepositoryAsync();
        var parent = Directory.CreateDirectory(Path.Combine(_root, "projects")).FullName;

        var result = await _clone.CloneAsync(source, parent, "app", Token);

        Assert.Null(result.Error);
        var path = Assert.IsType<string>(result.Path);
        Assert.Equal(Path.Combine(parent, "app"), path);
        Assert.True(Directory.Exists(Path.Combine(path, ".git")));
        Assert.Equal("hello\n", await File.ReadAllTextAsync(Path.Combine(path, "README.md"), Token));
    }

    [Fact]
    public async Task A_clone_that_fails_reports_what_git_said()
    {
        Assert.SkipWhen(!IsolatedGitLauncher.GitInstalled, "git isn't on PATH.");
        var parent = Directory.CreateDirectory(Path.Combine(_root, "projects")).FullName;
        var missing = Path.Combine(_root, "no-such-repo.git");

        var result = await _clone.CloneAsync(missing, parent, "app", Token);

        Assert.Null(result.Path);
        var error = Assert.IsType<string>(result.Error);
        Assert.DoesNotContain("Cloning into", error, StringComparison.Ordinal);
        Assert.DoesNotContain("fatal:", error, StringComparison.Ordinal);
        // "repository '…' does not exist", or "'…' does not appear to be a git repository", by version and system.
        Assert.Contains("repository", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(parent, "app")));
    }

    [Fact]
    public async Task A_folder_that_is_already_there_is_refused_before_git_runs()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_root, "projects")).FullName;
        Directory.CreateDirectory(Path.Combine(parent, "app"));

        var result = await _clone.CloneAsync("https://example.invalid/org/app.git", parent, "app", Token);

        Assert.Equal("There's already a folder named app there.", result.Error);
    }

    /// <summary>A repository with one commit, as a bare clone, which git clones over the file system.</summary>
    private async Task<string> MakeBareRepositoryAsync()
    {
        var work = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        await _launcher.RunAsync(work, "init", "-q");
        await File.WriteAllTextAsync(Path.Combine(work, "README.md"), "hello\n", Token);
        await _launcher.RunAsync(work, "add", "README.md");
        await _launcher.RunAsync(work, "-c", "user.email=t@example.com", "-c", "user.name=t", "commit", "-q", "-m", "init");
        var bare = Path.Combine(_root, "source.git");
        await _launcher.RunAsync(_root, "clone", "-q", "--bare", work, bare);
        return bare;
    }
}
