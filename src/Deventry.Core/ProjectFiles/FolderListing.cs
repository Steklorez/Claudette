using Deventry.Core.Composer;
using Deventry.Core.Files;
using Deventry.Core.Git;

namespace Deventry.Core.ProjectFiles;

/// <summary>An entry of a folder in the Project Files tab's explorer.</summary>
/// <param name="RelativePath">From the project's folder, with forward slashes.</param>
/// <param name="IsIgnored">Git ignores it (outside a repository: a folder the <c>@</c> list skips); drawn dimmed.</param>
/// <param name="IsLink">A symbolic link or junction.</param>
/// <param name="LeadsUp">A link to a folder above it in the tree, which isn't expanded, so the tree can't go round forever.</param>
/// <param name="IsHidden">A dotfile, or one the OS hides, or in a hidden folder; drawn dimmed.</param>
public sealed record TreeEntry(string Name, string FullPath, string RelativePath, bool IsFolder, bool IsIgnored = false, bool IsLink = false, bool LeadsUp = false,
    bool IsHidden = false);

/// <summary>
/// Reads a folder for the Project Files tab's explorer (DESIGN.md §18, "Project files", "The explorer"): folders first,
/// then files, by name ignoring case, without <c>.git</c> or files Deventry is still writing, and with what git ignores
/// and what's hidden marked.
/// </summary>
public static class FolderListing
{
    /// <summary>
    /// The entries of <paramref name="folder"/>, inside <paramref name="root"/>, without asking git; with everything in it
    /// hidden when <paramref name="parentHidden"/>.
    /// </summary>
    public static IReadOnlyList<TreeEntry> Read(string root, string folder, bool parentHidden = false)
    {
        var entries = new List<TreeEntry>();
        DirectoryInfo directory;
        IEnumerable<FileSystemInfo> children;
        try
        {
            directory = new DirectoryInfo(folder);
            children = directory.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false });
            foreach (var child in children)
            {
                if (child.Name == ".git" || AtomicFile.IsTemp(child.Name))
                {
                    continue;
                }
                var isFolder = (child.Attributes & FileAttributes.Directory) != 0;
                var isLink = child.LinkTarget is not null || (child.Attributes & FileAttributes.ReparsePoint) != 0;
                entries.Add(new TreeEntry(child.Name, child.FullName, Relative(root, child.FullName), isFolder, IsLink: isLink,
                    LeadsUp: isFolder && isLink && LinkLeadsUp(child, root), IsHidden: parentHidden || IsHidden(child)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return entries;
        }
        entries.Sort(Compare);
        return entries;
    }

    /// <summary>
    /// <see cref="Read"/>, with what git ignores marked; outside a repository, the folders the <c>@</c> list skips
    /// (<c>node_modules</c>, <c>bin</c>, <c>obj</c> and the like). Everything under an ignored folder is ignored too, and
    /// everything under a hidden one hidden.
    /// </summary>
    public static async Task<IReadOnlyList<TreeEntry>> ReadAsync(string root, string folder, GitWorkingTree git, bool inRepository, bool parentIgnored, bool parentHidden,
        CancellationToken cancellationToken = default)
    {
        var entries = await Task.Run(() => Read(root, folder, parentHidden), cancellationToken).ConfigureAwait(false);
        if (parentIgnored)
        {
            return [.. entries.Select(e => e with { IsIgnored = true })];
        }
        if (!inRepository)
        {
            return [.. entries.Select(e => e.IsFolder && ProjectFileIndex.SkippedFolders.Contains(e.Name) ? e with { IsIgnored = true } : e)];
        }
        var asked = entries.Select(e => e.IsFolder ? e.RelativePath + "/" : e.RelativePath).ToList();
        var ignored = asked.Count == 0 ? null : await git.CheckIgnoredAsync(root, asked, cancellationToken).ConfigureAwait(false);
        if (ignored is null)
        {
            return entries;
        }
        return [.. entries.Select(e => ignored.Contains(e.IsFolder ? e.RelativePath + "/" : e.RelativePath) || ignored.Contains(e.RelativePath) ? e with { IsIgnored = true } : e)];
    }

    /// <summary>
    /// Hidden: a name that starts with a dot, on every system, so a project's tree looks the same on each; or one the OS
    /// hides, Windows' Hidden attribute and macOS's hidden flag, which .NET reads as <see cref="FileAttributes.Hidden"/>.
    /// </summary>
    public static bool IsHidden(FileSystemInfo entry) => entry.Name.StartsWith('.') || (entry.Attributes & FileAttributes.Hidden) != 0;

    /// <summary>Folders first, then files; by name, ignoring case, then by case so the order is stable.</summary>
    public static int Compare(TreeEntry a, TreeEntry b)
    {
        if (a.IsFolder != b.IsFolder)
        {
            return a.IsFolder ? -1 : 1;
        }
        var byName = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        return byName != 0 ? byName : StringComparer.Ordinal.Compare(a.Name, b.Name);
    }

    /// <summary><paramref name="path"/> from <paramref name="root"/>, with forward slashes; the full path when it's outside.</summary>
    public static string Relative(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." ? "" : IsUp(relative) || Path.IsPathRooted(relative) ? path : relative.Replace('\\', '/');
    }

    /// <summary>A relative path that leads out of its folder: <c>..</c> or <c>../x</c>, but not a name such as <c>..data</c>.</summary>
    public static bool IsUp(string relative) =>
        relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || relative.StartsWith("..\\", StringComparison.Ordinal);

    private static bool LinkLeadsUp(FileSystemInfo link, string root)
    {
        try
        {
            if (link.ResolveLinkTarget(returnFinalTarget: true) is not { } target)
            {
                return false;
            }
            var to = Path.TrimEndingDirectorySeparator(target.FullName);
            var from = Path.TrimEndingDirectorySeparator(link.FullName);
            var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            // The folder it's in, or one above that: expanding it would show the tree inside itself.
            return from.StartsWith(to + Path.DirectorySeparatorChar, comparison) || string.Equals(from, to, comparison)
                || Path.TrimEndingDirectorySeparator(root).StartsWith(to + Path.DirectorySeparatorChar, comparison);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
