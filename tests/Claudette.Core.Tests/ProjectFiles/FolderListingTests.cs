using Claudette.Core.Git;
using Claudette.Core.ProjectFiles;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.ProjectFiles;

/// <summary>A folder's entries in the Project Files tab's explorer (DESIGN.md §18, "Project files", "The explorer").</summary>
public sealed class FolderListingTests : IDisposable
{
    private readonly TempFolder _temp = new("claudette-listing");

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Project => _temp.Combine("project");

    [Fact]
    public void Folders_come_first_then_files_by_name_ignoring_case()
    {
        Write("b.txt");
        Write("A.md");
        Write("c.cs");
        Write("Zeta/z.txt");
        Write("alpha/a.txt");

        var entries = FolderListing.Read(Project, Project);

        Assert.Equal(["alpha", "Zeta", "A.md", "b.txt", "c.cs"], entries.Select(e => e.Name));
        Assert.Equal([true, true, false, false, false], entries.Select(e => e.IsFolder));
    }

    [Fact]
    public void Entries_have_their_full_path_and_their_path_from_the_project_with_forward_slashes()
    {
        Write("src/game/Player.cpp");

        var entry = Assert.Single(FolderListing.Read(Project, Path.Combine(Project, "src", "game")));

        Assert.Equal("Player.cpp", entry.Name);
        Assert.Equal(Path.Combine(Project, "src", "game", "Player.cpp"), entry.FullPath);
        Assert.Equal("src/game/Player.cpp", entry.RelativePath);
        Assert.False(entry.IsFolder || entry.IsIgnored || entry.IsLink || entry.LeadsUp);
    }

    [Fact]
    public void Git_and_files_still_being_written_are_left_out()
    {
        Write(".git/HEAD");
        Write("notes.md");
        Write("notes.md.0123456789abcdef.tmp");
        Write(".gitignore");

        Assert.Equal([".gitignore", "notes.md"], FolderListing.Read(Project, Project).Select(e => e.Name));
    }

    [Fact]
    public void A_worktrees_git_file_is_left_out_too()
    {
        Write(".git");
        Write("a.txt");

        Assert.Equal(["a.txt"], FolderListing.Read(Project, Project).Select(e => e.Name));
    }

    [Fact]
    public void A_folder_that_isnt_there_has_no_entries()
    {
        Assert.Empty(FolderListing.Read(Project, Path.Combine(Project, "missing")));
        Assert.Empty(FolderListing.Read(Project, _temp.Write("file.txt", "")));
    }

    [Fact]
    public void Names_the_same_but_for_case_are_in_a_stable_order()
    {
        var upper = new TreeEntry("Readme.md", "/p/Readme.md", "Readme.md", IsFolder: false);
        var lower = new TreeEntry("readme.md", "/p/readme.md", "readme.md", IsFolder: false);
        var folder = new TreeEntry("zz", "/p/zz", "zz", IsFolder: true);

        Assert.True(FolderListing.Compare(upper, lower) < 0);
        Assert.True(FolderListing.Compare(lower, upper) > 0);
        Assert.Equal(0, FolderListing.Compare(upper, upper));
        Assert.True(FolderListing.Compare(folder, upper) < 0);
        Assert.True(FolderListing.Compare(lower, folder) > 0);
    }

    [Fact]
    public void A_path_outside_the_project_stays_whole()
    {
        var root = Path.Combine(Path.GetTempPath(), "project");

        Assert.Equal("", FolderListing.Relative(root, root));
        Assert.Equal("a/b.txt", FolderListing.Relative(root, Path.Combine(root, "a", "b.txt")));
        var sibling = Path.Combine(Path.GetTempPath(), "other", "c.txt");
        Assert.Equal(sibling, FolderListing.Relative(root, sibling));
    }

    [Fact]
    public void A_name_starting_with_two_dots_is_inside_the_project()
    {
        var root = Path.Combine(Path.GetTempPath(), "project");

        Assert.Equal("..data", FolderListing.Relative(root, Path.Combine(root, "..data")));
    }

    [Fact]
    public void A_link_to_a_folder_above_it_doesnt_lead_round_forever()
    {
        Directory.CreateDirectory(Path.Combine(Project, "sub"));
        var outside = _temp.CreateFolder("outside");
        using var up = FolderLinks.Create(Path.Combine(Project, "sub", "up"), Project);
        using var self = FolderLinks.Create(Path.Combine(Project, "sub", "self"), Path.Combine(Project, "sub"));
        using var across = FolderLinks.Create(Path.Combine(Project, "sub", "across"), outside);

        var entries = FolderListing.Read(Project, Path.Combine(Project, "sub")).ToDictionary(e => e.Name);

        Assert.All(entries.Values, e => Assert.True(e.IsFolder && e.IsLink));
        Assert.True(entries["up"].LeadsUp);
        Assert.True(entries["self"].LeadsUp);
        Assert.False(entries["across"].LeadsUp);
    }

    [Fact]
    public async Task Outside_a_repository_the_folders_the_mention_list_skips_are_ignored()
    {
        Write("node_modules/x/index.js");
        Write("bin/Debug/app.dll");
        Write("src/main.c");
        Write("obj");
        // Git isn't asked outside a repository: one that can't run changes nothing.
        var git = new GitWorkingTree(new IsolatedGitLauncher(_temp.Path), TimeProvider.System, $"claudette-no-such-git-{Guid.NewGuid():N}");

        var entries = await FolderListing.ReadAsync(Project, Project, git, inRepository: false, parentIgnored: false, Token);

        Assert.Equal([("bin", true), ("node_modules", true), ("src", false), ("obj", false)], entries.Select(e => (e.Name, e.IsIgnored)));
    }

    [Fact]
    public async Task Everything_under_an_ignored_folder_is_ignored_without_asking_git()
    {
        Write("build/out/app.o");
        Write("build/log.txt");
        var git = new GitWorkingTree(new IsolatedGitLauncher(_temp.Path), TimeProvider.System, $"claudette-no-such-git-{Guid.NewGuid():N}");

        var entries = await FolderListing.ReadAsync(Project, Path.Combine(Project, "build"), git, inRepository: true, parentIgnored: true, Token);

        Assert.Equal([("out", true), ("log.txt", true)], entries.Select(e => (e.Name, e.IsIgnored)));
    }

    [Fact]
    public async Task When_git_cant_say_nothing_is_marked_ignored()
    {
        Write("node_modules/x/index.js");
        Write("a.log");
        var git = new GitWorkingTree(new IsolatedGitLauncher(_temp.Path), TimeProvider.System, $"claudette-no-such-git-{Guid.NewGuid():N}");

        var entries = await FolderListing.ReadAsync(Project, Project, git, inRepository: true, parentIgnored: false, Token);

        Assert.Equal(["node_modules", "a.log"], entries.Select(e => e.Name));
        Assert.DoesNotContain(entries, e => e.IsIgnored);
    }

    [Fact]
    public async Task In_a_repository_git_says_what_it_ignores()
    {
        Assert.SkipWhen(!IsolatedGitLauncher.GitInstalled, "git isn't on PATH.");
        var launcher = new IsolatedGitLauncher(_temp.Path);
        Directory.CreateDirectory(Project);
        await launcher.RunAsync(Project, "init", "-q");
        Write(".gitignore", "build/\n*.log\n!keep.log\n");
        Write("build/out.o");
        Write("src/main.c");
        Write("src/debug.log");
        Write("app.log");
        Write("keep.log");
        Write("with space ü.log");
        Write("tracked.log");
        // A tracked file is never ignored, whatever .gitignore says.
        await launcher.RunAsync(Project, "add", "-f", "tracked.log");
        var git = new GitWorkingTree(launcher, TimeProvider.System);

        var top = await FolderListing.ReadAsync(Project, Project, git, inRepository: true, parentIgnored: false, Token);
        var src = await FolderListing.ReadAsync(Project, Path.Combine(Project, "src"), git, inRepository: true, parentIgnored: false, Token);

        Assert.Equal(
            [("build", true), ("src", false), (".gitignore", false), ("app.log", true), ("keep.log", false), ("tracked.log", false), ("with space ü.log", true)],
            top.Select(e => (e.Name, e.IsIgnored)));
        Assert.Equal([("debug.log", true), ("main.c", false)], src.Select(e => (e.Name, e.IsIgnored)));
    }

    private void Write(string relativePath, string content = "") => _temp.Write("project/" + relativePath, content);
}
