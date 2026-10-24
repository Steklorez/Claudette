namespace Claudette.Core.Git;

/// <summary>Cheap git facts read straight from the <c>.git</c> folder, without running git.</summary>
public static class GitInfo
{
    /// <summary>
    /// The current branch of the repository containing <paramref name="folder"/>, a short commit id when detached, or
    /// null when it isn't in a repository. Works for worktrees, where <c>.git</c> is a file pointing elsewhere.
    /// </summary>
    public static string? TryGetBranch(string folder)
    {
        try
        {
            return GitDirectory.Find(folder)?.ReadHeadName();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Unreadable: treat as not a repository.
            return null;
        }
    }

    /// <summary>
    /// What's checked out in the repository containing <paramref name="folder"/>: the branch (null on a detached HEAD)
    /// and the commit HEAD points at (null when it can't be read). Null when it isn't in a repository.
    /// </summary>
    public static (string? Branch, string? Commit)? TryGetHead(string folder)
    {
        try
        {
            return GitDirectory.Find(folder)?.ReadHead();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The working tree containing <paramref name="folder"/>: its top folder, its own git folder, which holds its HEAD
    /// and index (for a linked worktree, under the main checkout's <c>.git/worktrees</c>), and the git folder every
    /// worktree of the repository shares, which tells two of them from two repositories. Null when it isn't in one.
    /// </summary>
    public static (string WorkTree, string GitDir, string CommonDir)? TryGetWorkTree(string folder) =>
        GitDirectory.Find(folder) is { } found ? (found.WorkTree, found.GitDir, found.CommonDir) : null;
}
