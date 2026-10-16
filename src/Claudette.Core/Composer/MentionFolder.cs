using Claudette.Core.ProjectFiles;

namespace Claudette.Core.Composer;

/// <summary>
/// A folder the composer's <c>@</c> list reads from disk (DESIGN.md §5, "Autocomplete"), from a path typed after the
/// <c>@</c> up to its last slash: one outside the working folder (<c>C:/Work/</c>, <c>//server/share/</c>, <c>/etc/</c>,
/// <c>~/</c>, <c>../</c>), whose entries are all there is to list, or a folder inside it (<c>src/</c>), whose own
/// entries go ahead of the fuzzy matches.
/// </summary>
/// <param name="FullPath">The folder, as the OS writes it.</param>
/// <param name="Mention">
/// The folder as a mention writes it, with forward slashes and one at its end: absolute outside the working folder,
/// relative to it inside (empty for the working folder itself).
/// </param>
/// <param name="Prefix">What's typed after the folder: the start of a name in it.</param>
/// <param name="IsOutside">Typed as a path outside the working folder: absolute, from the home folder, or from the folder above.</param>
public sealed record MentionFolder(string FullPath, string Mention, string Prefix, bool IsOutside)
{
    /// <summary>The folder <paramref name="query"/> names, or null when it names none (no slash yet, or a path that can't be).</summary>
    /// <param name="query">What's typed after the <c>@</c>, without an opening quote.</param>
    /// <param name="home">The home folder, for <c>~/</c>.</param>
    public static MentionFolder? From(string query, string workingFolder, string home)
    {
        var typed = query.Replace('\\', '/');
        var slash = typed.LastIndexOf('/');
        if (slash < 0)
        {
            return null;
        }
        var folder = typed[..(slash + 1)];
        var prefix = typed[(slash + 1)..];
        string full;
        var outside = true;
        try
        {
            if (folder.StartsWith("~/", StringComparison.Ordinal))
            {
                full = Path.Join(home, folder[2..]);
            }
            else if (IsAbsolute(folder))
            {
                full = folder;
            }
            else
            {
                full = Path.Join(workingFolder, folder);
                outside = FolderListing.IsUp(folder);
            }
            // Without the slash it was typed with, except a drive's or the root's own.
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(full));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        if (!outside)
        {
            var relative = Path.GetRelativePath(workingFolder, full);
            // src/../../x/ leads out as surely as ../x/ does.
            if (!FolderListing.IsUp(relative) && !Path.IsPathRooted(relative))
            {
                return new MentionFolder(full, relative == "." ? "" : WithSlash(relative), prefix, IsOutside: false);
            }
        }
        return new MentionFolder(full, WithSlash(full), prefix, IsOutside: true);
    }

    /// <summary>
    /// A drive (<c>C:/</c>) or a share (<c>//server/</c>) on Windows, where a lone leading slash means the current
    /// drive's root and isn't taken as a path; a leading slash elsewhere.
    /// </summary>
    private static bool IsAbsolute(string folder) => OperatingSystem.IsWindows()
        ? (folder.Length >= 3 && char.IsAsciiLetter(folder[0]) && folder[1] == ':' && folder[2] == '/') || (folder.StartsWith("//", StringComparison.Ordinal) && folder.Length > 2)
        : folder.StartsWith('/');

    private static string WithSlash(string path)
    {
        path = path.Replace('\\', '/');
        return path.EndsWith('/') ? path : path + "/";
    }
}
