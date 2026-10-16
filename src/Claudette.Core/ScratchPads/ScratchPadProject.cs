using Claudette.Core.Git;

namespace Claudette.Core.ScratchPads;

/// <summary>
/// Which scratch pad a folder's tabs share (DESIGN.md §18, "Scratch pad"): one per project, by its <see cref="ProjectKey"/>.
/// A folder in a git repository with a remote shares the pad with every branch, worktree and clone of it, on any machine;
/// without a remote the pad is this machine's.
/// </summary>
/// <param name="Id">The pad's file name: a hash, so the library's file names don't name remotes or folders.</param>
/// <param name="Remote">The repository's remote in its normal form; null for a pad that stays on this machine.</param>
/// <param name="PathInRepo">The folder inside the repository, with forward slashes; <c>""</c> for its top or outside one.</param>
/// <param name="IsInRepository">The folder is in a git repository: why a pad that isn't shared isn't.</param>
public sealed record ScratchPadProject(string Id, string? Remote, string PathInRepo, bool IsInRepository)
{
    /// <summary>The pad syncs through the session library.</summary>
    public bool IsShared => Remote is not null;

    public static ScratchPadProject For(string folder)
    {
        var project = ProjectKey.For(folder);
        return new ScratchPadProject(project.Key, project.Remote, project.PathInRepo, project.IsInRepository);
    }
}
