using Claudette.Core.Files;

namespace Claudette.Core.ProjectFiles;

/// <summary>
/// A file or folder renamed from the Project Files tab's explorer (DESIGN.md §18, "Project files", "Renaming and
/// deleting"): the new name checked as it's typed, as a new entry's is, then the rename, in its own folder and never over
/// anything there.
/// </summary>
public static class RenameEntry
{
    /// <summary>
    /// What renaming <paramref name="path"/> to <paramref name="typed"/> makes: the new path, or why it can't be used.
    /// Neither for nothing to do: a name left empty, or as it was. Spaces around it are dropped.
    /// </summary>
    public static NewEntryName Check(string path, string typed) => Check(path, typed, OperatingSystem.IsWindows());

    internal static NewEntryName Check(string path, string typed, bool windows)
    {
        var name = typed.Trim();
        var old = Path.GetFileName(path);
        if (name.Length == 0 || string.Equals(name, old, StringComparison.Ordinal))
        {
            return new NewEntryName(null, null);
        }
        // Renaming keeps it in its folder: moving it is another thing.
        if (name.Contains('/') || (windows && name.Contains('\\')))
        {
            return new NewEntryName(null, $"A name can't have {(name.Contains('/') ? '/' : '\\')} in it.");
        }
        if (NewEntry.NameProblem(name, windows) is { } problem)
        {
            return new NewEntryName(null, problem);
        }
        var folder = Path.GetDirectoryName(path)!;
        var target = Path.Combine(folder, name);
        if (IsTaken(folder, target, name, old))
        {
            return new NewEntryName(null, $"There's already a {(Directory.Exists(target) ? "folder" : "file")} called {name} here.");
        }
        return new NewEntryName(target, null);
    }

    /// <summary>
    /// Something else has the name. When only its case changes, the name is there on a disk that ignores case (Windows',
    /// and the Mac's usually) only because it's this one; on one that doesn't, the folder has another entry by that name.
    /// </summary>
    private static bool IsTaken(string folder, string target, string name, string old)
    {
        if (!File.Exists(target) && !Directory.Exists(target))
        {
            return false;
        }
        if (!string.Equals(name, old, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        try
        {
            return Directory.EnumerateFileSystemEntries(folder).Any(e => string.Equals(Path.GetFileName(e), name, StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Renames <paramref name="path"/> (a link itself, not what it leads to) to <paramref name="newPath"/>, never over
    /// something there. A change of case alone goes by way of a name of its own, which a disk that ignores case would
    /// otherwise take to be there already. Null when it's renamed, or why it couldn't be.
    /// </summary>
    public static string? Rename(string path, string newPath)
    {
        string? between = null;
        try
        {
            var isFolder = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
            if (string.Equals(path, newPath, StringComparison.OrdinalIgnoreCase))
            {
                between = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(newPath)}.{Guid.NewGuid():N}.renaming");
                Move(path, between, isFolder);
                Move(between, newPath, isFolder, back: path);
                return null;
            }
            Move(path, newPath, isFolder);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"Claudette couldn't rename it. {ex.Message}";
        }
    }

    /// <summary>Moves it, trying again while another program has it open; with <paramref name="back"/>, puts it back there if it can't.</summary>
    private static void Move(string from, string to, bool isFolder, string? back = null)
    {
        try
        {
            FileRetry.Run(() =>
            {
                if (isFolder)
                {
                    Directory.Move(from, to);
                }
                else
                {
                    File.Move(from, to, overwrite: false);
                }
            });
        }
        catch when (back is not null)
        {
            Move(from, back, isFolder);
            throw;
        }
    }
}
