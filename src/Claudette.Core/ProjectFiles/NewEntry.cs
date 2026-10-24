namespace Claudette.Core.ProjectFiles;

/// <summary>What a name typed for a new file or folder makes.</summary>
/// <param name="Path">Where it's made; null when nothing was typed, or it can't be made.</param>
/// <param name="Problem">Why it can't be made, in words for the box; null when it can.</param>
public sealed record NewEntryName(string? Path, string? Problem);

/// <summary>
/// A new file or folder made from the Project Files tab's explorer (DESIGN.md §18, "Project files", "New files and
/// folders"): the name typed checked as it's typed, then the file (empty) or the folder made, with the folders on its way.
/// </summary>
public static class NewEntry
{
    /// <summary>The names Windows keeps for devices, with or without an extension.</summary>
    private static readonly HashSet<string> WindowsDevices = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>
    /// What <paramref name="typed"/> makes in <paramref name="folder"/>. Slashes make the folders on the way
    /// (<c>AI/Brain.cpp</c>), and spaces around the whole are dropped.
    /// </summary>
    public static NewEntryName Check(string folder, string typed) => Check(folder, typed, OperatingSystem.IsWindows());

    internal static NewEntryName Check(string folder, string typed, bool windows)
    {
        var parts = typed.Trim().Split(windows ? ['/', '\\'] : ['/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return new NewEntryName(null, null);
        }
        foreach (var part in parts)
        {
            if (NameProblem(part, windows) is { } problem)
            {
                return new NewEntryName(null, problem);
            }
        }
        var path = folder;
        for (var i = 0; i < parts.Length; i++)
        {
            path = Path.Combine(path, parts[i]);
            var shown = string.Join('/', parts[..(i + 1)]);
            if (i < parts.Length - 1 && File.Exists(path))
            {
                return new NewEntryName(null, $"{shown} is a file, so nothing can go in it.");
            }
            if (i == parts.Length - 1 && (File.Exists(path) || Directory.Exists(path)))
            {
                return new NewEntryName(null, $"There's already a {(Directory.Exists(path) ? "folder" : "file")} called {shown} here.");
            }
        }
        return new NewEntryName(path, null);
    }

    private static string? NameProblem(string name, bool windows)
    {
        if (name is "." or "..")
        {
            return "A name can't be \".\" or \"..\".";
        }
        foreach (var c in name)
        {
            if (char.IsControl(c))
            {
                return "A name can't have a tab, a line break or another control character in it.";
            }
            if (windows && c is '<' or '>' or ':' or '"' or '|' or '?' or '*')
            {
                return $"A name can't have {c} in it.";
            }
        }
        if (windows && (name.EndsWith(' ') || name.EndsWith('.')))
        {
            return "A name can't end with a space or a dot.";
        }
        if (windows && WindowsDevices.Contains(name.Split('.')[0].TrimEnd()))
        {
            return $"{name} is a name Windows keeps for itself.";
        }
        return null;
    }

    /// <summary>Makes the file, empty, or the folder, with the folders on its way. Null when it's made, or why it couldn't be.</summary>
    public static string? Make(string path, bool isFolder)
    {
        try
        {
            if (isFolder)
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Not written over if it appeared since the name was checked.
                new FileStream(path, FileMode.CreateNew, FileAccess.Write).Dispose();
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"Claudette couldn't make it. {ex.Message}";
        }
    }
}
