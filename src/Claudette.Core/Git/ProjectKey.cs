using System.Security.Cryptography;
using System.Text;
using Claudette.Core.Settings;

namespace Claudette.Core.Git;

/// <summary>
/// A folder's project, as one key that's the same on every machine where it can be (DESIGN.md §9): a folder in a git
/// repository with a remote is known by the remote and the folder's path inside the repository, so every branch, worktree
/// and clone of it shares the key. Without a remote the key is this machine's: a repository's is known by its git folder
/// and the path inside it, so its worktrees still share it, and any other folder's by its path. Read from files only; no
/// git process. Scratch pads (<c>ScratchPadProject</c>) and usage by project (DESIGN.md §6) both go by it.
/// </summary>
/// <param name="Key">A hash, so files in the session library don't name remotes or folders.</param>
/// <param name="Remote">The repository's remote in its normal form; null for a key that's this machine's only.</param>
/// <param name="PathInRepo">The folder inside the repository, with forward slashes; <c>""</c> for its top or outside one.</param>
/// <param name="IsInRepository">The folder is in a git repository.</param>
public sealed record ProjectKey(string Key, string? Remote, string PathInRepo, bool IsInRepository)
{
    /// <summary>The key is the same on another machine with a clone of the project.</summary>
    public bool IsShared => Remote is not null;

    public static ProjectKey For(string folder)
    {
        if (GitDirectory.Find(folder) is { } repository)
        {
            try
            {
                var pathInRepo = ProjectIdentity.RelativePath(repository.WorkTree, folder);
                // Folders are compared without case, as git on Windows and macOS does; a remote already is.
                var inRepo = pathInRepo.ToLowerInvariant();
                return repository.ReadRemoteUrl() is { } url && ProjectIdentity.NormalizeRemote(url) is { Length: > 0 } remote
                    ? new ProjectKey(Hash($"git\n{remote}\n{inRepo}"), remote, pathInRepo, IsInRepository: true)
                    : new ProjectKey(Hash($"repository\n{PathKey(repository.CommonDir)}\n{inRepo}"), null, pathInRepo, IsInRepository: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Its files can't be read: the folder's own key.
            }
        }
        return new ProjectKey(Hash($"folder\n{PathKey(folder)}"), null, "", IsInRepository: false);
    }

    /// <summary>A folder's path as one key, however it was written: Windows' paths are compared without case.</summary>
    private static string PathKey(string folder)
    {
        var normalized = FolderHistory.Normalize(folder).Replace('\\', '/');
        return OperatingSystem.IsWindows() ? normalized.ToLowerInvariant() : normalized;
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}
