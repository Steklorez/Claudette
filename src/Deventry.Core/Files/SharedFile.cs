namespace Deventry.Core.Files;

/// <summary>
/// Reads a file another program writes, Claude Code's own say, without getting in its way: opened so the other program
/// can still write it or delete it while it's read. On Windows a file read with <see cref="File.ReadAllText(string)"/>
/// can't be written or deleted by anyone else until the read is done, so a write Claude Code made at that moment failed.
/// Renaming over a file that's open fails on Windows whatever the reader allows, so it's read at once and let go.
/// </summary>
public static class SharedFile
{
    public static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>As <see cref="File.ReadAllText(string)"/>: UTF-8, or what a byte order mark says.</summary>
    public static string ReadAllText(string path)
    {
        using var reader = new StreamReader(OpenRead(path));
        return reader.ReadToEnd();
    }
}
