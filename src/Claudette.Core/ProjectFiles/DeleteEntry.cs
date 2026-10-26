using Claudette.Core.Files;

namespace Claudette.Core.ProjectFiles;

/// <summary>
/// What deleting from the Project Files tab's explorer needs besides the trash (DESIGN.md §18, "Project files", "Renaming
/// and deleting"): how many files a folder holds, for the question, and deleting for good where there's no trash.
/// </summary>
public static class DeleteEntry
{
    /// <summary>Folders and files, hidden ones too, but not what a link leads to: a link goes as itself.</summary>
    private static readonly EnumerationOptions AllButLinks = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>How many files <paramref name="folder"/> holds, all the way down, counting no further than <paramref name="cap"/>.</summary>
    public static int CountFiles(string folder, int cap)
    {
        var count = 0;
        try
        {
            foreach (var _ in Directory.EnumerateFiles(folder, "*", AllButLinks))
            {
                if (++count >= cap)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // What could be counted.
        }
        return count;
    }

    /// <summary>
    /// Deletes <paramref name="path"/> for good: a file, a link (not what it leads to), or a folder with everything in it,
    /// read-only files too, which Windows otherwise won't delete. Null when it's gone, or why it couldn't be.
    /// </summary>
    public static string? DeletePermanently(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                ClearReadOnly(path, attributes);
                FileRetry.Run(() => File.Delete(path));
            }
            else if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                FileRetry.Run(() => Directory.Delete(path));
            }
            else
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", AllButLinks))
                {
                    ClearReadOnly(file, File.GetAttributes(file));
                }
                FileRetry.Run(() => Directory.Delete(path, recursive: true));
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"Claudette couldn't delete it. {ex.Message}";
        }
    }

    private static void ClearReadOnly(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
    }
}
