using Claudette.Core.Git;
using Claudette.Core.ProjectFiles;

namespace Claudette.Core.Composer;

/// <summary>
/// Reads the folders the composer's <c>@</c> list shows from disk (DESIGN.md §5, "Autocomplete"): each once, kept for
/// <see cref="MaxAge"/>, so typing a name in a folder never reads it again. One for the whole app.
/// </summary>
public sealed class MentionFolderReader(GitWorkingTree git, TimeProvider timeProvider)
{
    /// <summary>The most folders kept at once; the oldest goes first.</summary>
    private const int MaxKept = 16;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (DateTimeOffset At, Task<IReadOnlyList<TreeEntry>?> Entries)> _kept = new(StringComparer.Ordinal);

    /// <summary>How long a folder's entries are used before it's read again.</summary>
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The folder's entries, folders first, then files, by name; null when there's no such folder. Read off the
    /// caller's thread. A folder inside <paramref name="project"/>'s has what git ignores marked (outside a repository,
    /// the folders the <c>@</c> list skips), and git is only asked about the entries the project's listing doesn't
    /// have.
    /// </summary>
    /// <param name="project">The working folder's listing, for a folder inside it; null for one outside.</param>
    public Task<IReadOnlyList<TreeEntry>?> ReadAsync(MentionFolder folder, ProjectFileIndex? project)
    {
        var key = (project is null ? "" : project.Folder) + "|" + folder.FullPath;
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (_kept.TryGetValue(key, out var kept) && now - kept.At < MaxAge)
            {
                return kept.Entries;
            }
            if (_kept.Count >= MaxKept)
            {
                _kept.Remove(_kept.MinBy(k => k.Value.At).Key);
            }
            var entries = Task.Run(() => ReadFreshAsync(folder, project));
            _kept[key] = (now, entries);
            return entries;
        }
    }

    private async Task<IReadOnlyList<TreeEntry>?> ReadFreshAsync(MentionFolder folder, ProjectFileIndex? project)
    {
        if (!Directory.Exists(folder.FullPath))
        {
            return null;
        }
        var entries = FolderListing.Read(project?.Folder ?? folder.FullPath, folder.FullPath);
        if (project is null || entries.Count == 0)
        {
            return entries;
        }
        return project.FromGit ? await MarkIgnoredByGitAsync(folder, project, entries).ConfigureAwait(false) : MarkSkipped(folder, entries);
    }

    /// <summary>
    /// What's in the project's listing isn't ignored. Of the rest, a folder git ignores has nothing but ignored entries,
    /// and otherwise git is asked about each.
    /// </summary>
    private async Task<IReadOnlyList<TreeEntry>> MarkIgnoredByGitAsync(MentionFolder folder, ProjectFileIndex project, IReadOnlyList<TreeEntry> entries)
    {
        if (folder.Mention.Length > 0 && !project.Contains(folder.Mention)
            && await git.CheckIgnoredAsync(project.Folder, [folder.Mention]).ConfigureAwait(false) is { Count: > 0 })
        {
            return [.. entries.Select(e => e with { IsIgnored = true })];
        }
        var unknown = entries.Select(PathOf).Where(p => !project.Contains(p)).ToArray();
        if (unknown.Length == 0 || await git.CheckIgnoredAsync(project.Folder, unknown).ConfigureAwait(false) is not { } ignored)
        {
            return entries;
        }
        return [.. entries.Select(e => ignored.Contains(PathOf(e)) || ignored.Contains(e.RelativePath) ? e with { IsIgnored = true } : e)];
    }

    /// <summary>Outside a repository: the folders the <c>@</c> list skips, and everything in one.</summary>
    private static IReadOnlyList<TreeEntry> MarkSkipped(MentionFolder folder, IReadOnlyList<TreeEntry> entries)
    {
        var inSkipped = folder.Mention.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(ProjectFileIndex.SkippedFolders.Contains);
        return [.. entries.Select(e => inSkipped || (e.IsFolder && ProjectFileIndex.SkippedFolders.Contains(e.Name)) ? e with { IsIgnored = true } : e)];
    }

    /// <summary>As the project's listing has it: a folder's ends in a slash.</summary>
    private static string PathOf(TreeEntry entry) => entry.IsFolder ? entry.RelativePath + "/" : entry.RelativePath;
}
