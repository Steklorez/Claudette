using Claudette.Core.Composer;
using Claudette.Core.Git;
using Claudette.Core.Processes;
using Claudette.Core.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Claudette.Core.Tests.Composer;

/// <summary>
/// The folders the composer's <c>@</c> list reads from disk: a path typed outside the working folder, or a folder inside
/// it, with what git ignores marked (DESIGN.md §5, "Autocomplete").
/// </summary>
public sealed class MentionFolderTests : IDisposable
{
    private readonly TempFolder _root = new("claudette-mention-folders");

    public void Dispose() => _root.Dispose();

    private string Work => _root.Combine("work");

    private string Home => _root.Combine("home");

    /// <summary>A folder's full path as a mention writes it.</summary>
    private static string AsMention(string path) => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/') + "/";

    // ---- Reading what's typed ---------------------------------------------------------------------------------

    [Fact]
    public void Without_a_slash_no_folder_is_typed_yet()
    {
        Assert.Null(MentionFolder.From("", Work, Home));
        Assert.Null(MentionFolder.From("src", Work, Home));
        Assert.Null(MentionFolder.From("~", Work, Home));
    }

    [Fact]
    public void A_folder_inside_the_working_folder_is_relative_to_it()
    {
        var folder = MentionFolder.From("src/deep/no", Work, Home);

        Assert.Equal(new MentionFolder(Path.Combine(Work, "src", "deep"), "src/deep/", "no", IsOutside: false), folder);
        Assert.Equal("src/", MentionFolder.From("./src/", Work, Home)?.Mention);
        Assert.Equal("", MentionFolder.From("./", Work, Home)?.Mention);
    }

    [Fact]
    public void The_folder_above_is_outside_and_written_in_full()
    {
        var folder = MentionFolder.From("../other/no", Work, Home)!;

        Assert.True(folder.IsOutside);
        Assert.Equal(_root.Combine("other"), folder.FullPath);
        Assert.Equal(AsMention(_root.Combine("other")), folder.Mention);
        Assert.Equal("no", folder.Prefix);
        // A path that only leads out further along is outside too.
        Assert.Equal(_root.Combine("other"), MentionFolder.From("src/../../other/", Work, Home)?.FullPath);
        Assert.True(MentionFolder.From("src/../../other/", Work, Home)?.IsOutside);
    }

    [Fact]
    public void A_tilde_is_the_home_folder()
    {
        var folder = MentionFolder.From("~/Documents/re", Work, Home)!;

        Assert.True(folder.IsOutside);
        Assert.Equal(Path.Combine(Home, "Documents"), folder.FullPath);
        Assert.Equal(AsMention(Path.Combine(Home, "Documents")), folder.Mention);
        Assert.Equal("re", folder.Prefix);
    }

    [Fact]
    public void On_Windows_a_drive_or_share_is_absolute_with_either_slash()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Drives and shares are Windows paths.");

        Assert.Equal(new MentionFolder(@"C:\Work\Game", "C:/Work/Game/", "Sou", IsOutside: true), MentionFolder.From(@"C:\Work\Game\Sou", Work, Home));
        Assert.Equal("C:/", MentionFolder.From("C:/", Work, Home)?.Mention);
        Assert.Equal(@"C:\", MentionFolder.From("C:/", Work, Home)?.FullPath);
        Assert.Equal(@"\\server\share\docs", MentionFolder.From(@"\\server\share\docs\a", Work, Home)?.FullPath);
        Assert.Equal("//server/share/docs/", MentionFolder.From("//server/share/docs/a", Work, Home)?.Mention);
        // A lone slash would be the current drive's root, which nobody means: it stays in the working folder.
        Assert.Equal(new MentionFolder(Work, "", "", IsOutside: false), MentionFolder.From("/", Work, Home));
    }

    [Fact]
    public void On_macOS_and_Linux_a_leading_slash_is_absolute()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A leading slash is the root only on macOS and Linux.");

        Assert.Equal(new MentionFolder("/etc", "/etc/", "ho", IsOutside: true), MentionFolder.From("/etc/ho", Work, Home));
        Assert.Equal("/", MentionFolder.From("/", Work, Home)?.Mention);
    }

    // ---- Reading the folder -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_folder_outside_lists_folders_then_files_and_a_missing_one_is_null()
    {
        _root.Write("other/zeta.txt", "");
        _root.Write("other/Alpha.md", "");
        _root.Write("other/beta/inner.txt", "");
        _root.Write("other/.git/HEAD", "");
        var reader = Reader(TimeProvider.System, out _);

        var entries = await reader.ReadAsync(MentionFolder.From("../other/", Work, Home)!, project: null);

        Assert.Equal(["beta", "Alpha.md", "zeta.txt"], entries!.Select(e => e.Name));
        Assert.True(entries![0].IsFolder);
        Assert.DoesNotContain(entries!, e => e.IsIgnored);
        Assert.Null(await reader.ReadAsync(MentionFolder.From("../missing/", Work, Home)!, project: null));
    }

    [Fact]
    public async Task A_folder_is_read_once_until_it_is_stale()
    {
        _root.Write("other/one.txt", "");
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-16T12:00:00Z"));
        var reader = Reader(time, out _);
        var folder = MentionFolder.From("../other/o", Work, Home)!;

        Assert.Single((await reader.ReadAsync(folder, project: null))!);
        _root.Write("other/two.txt", "");
        // Typing on in the same folder reads nothing again.
        Assert.Single((await reader.ReadAsync(folder with { Prefix = "on" }, project: null))!);

        time.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal(2, (await reader.ReadAsync(folder, project: null))!.Count);
    }

    [Fact]
    public async Task Inside_a_repository_what_git_ignores_is_marked_and_git_is_only_asked_about_the_rest()
    {
        Assert.SkipWhen(!IsolatedGitLauncher.GitInstalled, "git isn't on PATH.");
        var launcher = new IsolatedGitLauncher(_root.Path);
        Directory.CreateDirectory(Work);
        await launcher.RunAsync(Work, "init", "-q");
        _root.Write("work/.gitignore", "build/\n*.log\n");
        _root.Write("work/src/app.cs", "");
        _root.Write("work/src/notes.log", "");
        _root.Write("work/src/sub/deep.cs", "");
        _root.Write("work/build/out.dll", "");
        _root.Write("work/build/Debug/app.pdb", "");
        var reader = Reader(TimeProvider.System, out var counting, launcher);
        var git = new GitWorkingTree(counting, TimeProvider.System);
        var project = new ProjectFileIndex(Work, git, TimeProvider.System);
        await project.GetAsync();

        counting.CheckIgnores = 0;
        var src = await reader.ReadAsync(MentionFolder.From("src/", Work, Home)!, project);

        Assert.Equal([("sub", false), ("app.cs", false), ("notes.log", true)], src!.Select(e => (e.Name, e.IsIgnored)));
        // Only notes.log wasn't in the project's listing.
        Assert.Equal(1, counting.CheckIgnores);

        counting.CheckIgnores = 0;
        var build = await reader.ReadAsync(MentionFolder.From("build/", Work, Home)!, project);

        Assert.Equal([("Debug", true), ("out.dll", true)], build!.Select(e => (e.Name, e.IsIgnored)));
        // The folder is ignored, so everything in it is, without asking about each.
        Assert.Equal(1, counting.CheckIgnores);
    }

    [Fact]
    public async Task Outside_a_repository_the_folders_the_list_skips_are_marked()
    {
        _root.Write("work/src/app.cs", "");
        _root.Write("work/node_modules/lib/index.js", "");
        var noGit = new GitWorkingTree(new IsolatedGitLauncher(_root.Path), TimeProvider.System, $"claudette-no-such-git-{Guid.NewGuid():N}");
        var project = new ProjectFileIndex(Work, noGit, TimeProvider.System);
        await project.GetAsync();
        var reader = new MentionFolderReader(noGit, TimeProvider.System);

        var top = await reader.ReadAsync(MentionFolder.From("./", Work, Home)!, project);
        var inside = await reader.ReadAsync(MentionFolder.From("node_modules/lib/", Work, Home)!, project);

        Assert.Equal([("node_modules", true), ("src", false)], top!.Select(e => (e.Name, e.IsIgnored)));
        Assert.Equal([("index.js", true)], inside!.Select(e => (e.Name, e.IsIgnored)));
    }

    [Fact]
    public async Task The_project_listing_knows_its_paths()
    {
        _root.Write("work/src/app.cs", "");
        _root.Write("work/readme.md", "");
        var noGit = new GitWorkingTree(new IsolatedGitLauncher(_root.Path), TimeProvider.System, $"claudette-no-such-git-{Guid.NewGuid():N}");
        var project = new ProjectFileIndex(Work, noGit, TimeProvider.System);

        Assert.False(project.Contains("readme.md"));
        await project.GetAsync();

        Assert.True(project.Contains("readme.md"));
        Assert.True(project.Contains("src/"));
        Assert.True(project.Contains("src/app.cs"));
        Assert.False(project.Contains("src"));
        Assert.False(project.Contains("missing.txt"));
    }

    private MentionFolderReader Reader(TimeProvider time, out CountingLauncher counting, IsolatedGitLauncher? launcher = null)
    {
        counting = new CountingLauncher(launcher ?? new IsolatedGitLauncher(_root.Path));
        return new MentionFolderReader(new GitWorkingTree(counting, time), time) { MaxAge = TimeSpan.FromSeconds(10) };
    }

    /// <summary>Counts the <c>git check-ignore</c> runs.</summary>
    private sealed class CountingLauncher(IProcessLauncher inner) : IProcessLauncher
    {
        public int CheckIgnores;

        public IRunningProcess Start(ProcessStartSpec spec)
        {
            if (spec.Arguments.Contains("check-ignore"))
            {
                Interlocked.Increment(ref CheckIgnores);
            }
            return inner.Start(spec);
        }
    }
}
