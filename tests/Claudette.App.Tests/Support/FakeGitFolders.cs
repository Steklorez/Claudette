using Claudette.Core.Git;

namespace Claudette.App.Tests.Support;

/// <summary>
/// Git's folders written by hand, which is all Claudette reads to find a working tree and its branch (Core's
/// <c>GitInfo</c>): no git runs.
/// </summary>
internal static class FakeGitFolders
{
    /// <summary>A repository at <paramref name="folder"/>, its HEAD on <paramref name="branch"/>.</summary>
    public static void Repository(string folder, string branch)
    {
        Directory.CreateDirectory(Path.Combine(folder, ".git"));
        File.WriteAllText(Path.Combine(folder, ".git", "HEAD"), $"ref: refs/heads/{branch}\n");
    }

    /// <summary>A worktree Claude Code made of the repository at <paramref name="main"/>, on <c>worktree-&lt;name&gt;</c>. Returns its folder.</summary>
    public static string Worktree(string main, string name)
    {
        var gitDir = Path.Combine(main, ".git", "worktrees", name);
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), $"ref: refs/heads/worktree-{name}\n");
        File.WriteAllText(Path.Combine(gitDir, "commondir"), "../..\n");
        var worktree = GitWorktrees.PathFor(main, name);
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {gitDir}\n");
        return worktree;
    }
}
