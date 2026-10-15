using Claudette.Core.Composer;

namespace Claudette.Core.Protocol;

/// <summary>
/// Where the file a reply names is (DESIGN.md §18, "Files named in Claude's replies"). A path is read against the tab's
/// folder, then its extra folders. A name, or a path that isn't there, is looked for among the files the session's
/// tools worked on, newest first, then among the project's (<see cref="ProjectFileIndex"/>); a name can match several.
/// <para>
/// Inline code becomes a link only once its file is found, which happens as the reply is shown, without a click. A
/// reply can be prompt-injected, and some paths reach the network just by being looked up (<c>/net/&lt;host&gt;</c> on
/// macOS), so that lookup only touches the tab's folders, the user's home and, on Windows, the computer's own drives.
/// A path elsewhere counts only when the session's tools named it. A click on a link looks wherever it points, except a
/// network path, whose server Windows would hand the user's credentials.
/// </para>
/// </summary>
/// <param name="folder">The tab's folder (its worktree, for a tab in one).</param>
/// <param name="extraFolders">The folders the tab added beside it.</param>
/// <param name="home">The user's home, for <c>~/</c>.</param>
/// <param name="index">The project's files, or null when there's no listing to look in.</param>
public sealed class ReplyFileFinder(Func<string> folder, Func<IReadOnlyList<string>> extraFolders, string home, Func<ProjectFileIndex?> index)
{
    /// <summary>At most this many files match a name; the rest aren't offered.</summary>
    public const int MaxMatches = 12;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// The files and folders <paramref name="reference"/> names, the likeliest first; empty when there are none.
    /// <paramref name="sessionFiles"/> are the paths the session's tools named, oldest first. With
    /// <paramref name="clicked"/>, a path is looked for wherever it points; without, only where looking can't reach
    /// another machine. The tab's folders and listing are read on the caller's thread, the disk off it.
    /// </summary>
    public Task<IReadOnlyList<string>> FindAsync(ReplyFileReference reference, IReadOnlyList<string> sessionFiles, bool clicked, CancellationToken cancellationToken = default)
    {
        var roots = Roots();
        var files = index();
        return Task.Run(() => FindCoreAsync(reference, roots, files, sessionFiles, clicked, cancellationToken), cancellationToken);
    }

    private async Task<IReadOnlyList<string>> FindCoreAsync(ReplyFileReference reference, List<string> roots, ProjectFileIndex? files,
        IReadOnlyList<string> sessionFiles, bool clicked, CancellationToken cancellationToken)
    {
        var path = reference.Path.Replace('\\', System.IO.Path.DirectorySeparatorChar).Replace('/', System.IO.Path.DirectorySeparatorChar);
        if (path.StartsWith('~') && (path.Length == 1 || path[1] == System.IO.Path.DirectorySeparatorChar))
        {
            path = home + path[1..];
        }
        // A path where it says: from the folder, or from an extra one.
        IEnumerable<string> candidates = System.IO.Path.IsPathRooted(path) ? [path] : roots.Select(root => System.IO.Path.Combine(root, path));
        foreach (var candidate in candidates)
        {
            if (Full(candidate) is { } full && (clicked || MayLookAt(full, roots) || Named(full, sessionFiles)) && Exists(full))
            {
                return [full];
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        // A name, or the end of a path: among the files the session worked on, newest first, then the project's.
        var tail = path.Trim(System.IO.Path.DirectorySeparatorChar, '.');
        if (tail.Length == 0 || System.IO.Path.IsPathRooted(tail))
        {
            return [];
        }
        // The session's tools named these, so looking at them reaches nothing new.
        var worked = new List<string>();
        for (var i = sessionFiles.Count - 1; i >= 0 && worked.Count < MaxMatches; i--)
        {
            if (Full(sessionFiles[i]) is { } full && EndsWith(full, tail) && !worked.Contains(full, PathComparer) && Exists(full))
            {
                worked.Add(full);
            }
        }
        if (worked.Count > 0)
        {
            return worked;
        }
        if (files is null)
        {
            return [];
        }
        var listed = await files.GetAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var slashed = tail.Replace(System.IO.Path.DirectorySeparatorChar, '/');
        return [.. listed
            .Where(p => EndsWith(p.IsFolder ? p.Path[..^1] : p.Path, slashed, '/'))
            .OrderBy(p => p.Depth).ThenBy(p => p.Path, StringComparer.Ordinal)
            .Take(MaxMatches)
            .Select(p => Full(System.IO.Path.Combine(files.Folder, p.Path.TrimEnd('/'))))
            .OfType<string>()];
    }

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>The tab's folder, then its extra folders, as full paths without a trailing separator.</summary>
    private List<string> Roots() => [.. new[] { folder() }.Concat(extraFolders()).Select(Full).OfType<string>()];

    /// <summary>The full path, without a trailing separator, or null for one that isn't a path, or that's on the network.</summary>
    private static string? Full(string path)
    {
        try
        {
            var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
            return full.Length >= 2 && full[0] is '\\' or '/' && full[1] is '\\' or '/' ? null : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="full"/> may be looked up before a click: inside the tab's folders or the user's home, or
    /// on one of the computer's own drives on Windows, where a mapped network drive is the only kind that isn't.
    /// </summary>
    private bool MayLookAt(string full, List<string> roots)
    {
        if (roots.Append(System.IO.Path.TrimEndingDirectorySeparator(home)).Any(root => IsInside(full, root)))
        {
            return true;
        }
        if (!OperatingSystem.IsWindows() || System.IO.Path.GetPathRoot(full) is not { Length: > 0 } drive)
        {
            return false;
        }
        try
        {
            return new DriveInfo(drive).DriveType == DriveType.Fixed;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsInside(string full, string root) =>
        full.Equals(root, PathComparison) || full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, PathComparison);

    /// <summary>The session's tools named <paramref name="full"/>, so it's been looked at already.</summary>
    private static bool Named(string full, IReadOnlyList<string> sessionFiles) =>
        sessionFiles.Any(f => Full(f) is { } named && PathComparer.Equals(named, full));

    /// <summary><paramref name="path"/> is <paramref name="tail"/>, or ends with it after a separator.</summary>
    private static bool EndsWith(string path, string tail, char separator = '\0')
    {
        separator = separator == '\0' ? System.IO.Path.DirectorySeparatorChar : separator;
        return path.Equals(tail, PathComparison)
            || path.Length > tail.Length && path[^(tail.Length + 1)] == separator && path.EndsWith(tail, PathComparison);
    }

    private static bool Exists(string full)
    {
        try
        {
            return File.Exists(full) || Directory.Exists(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
