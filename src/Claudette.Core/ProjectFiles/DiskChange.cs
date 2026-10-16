namespace Claudette.Core.ProjectFiles;

/// <summary>What a change to an open file on disk does to it in the editor.</summary>
public enum DiskChangeAction
{
    /// <summary>Nothing: the file is still what the editor read.</summary>
    None,

    /// <summary>Read it again, where it was: there are no edits to lose.</summary>
    Reload,

    /// <summary>Ask, in a banner: it changed under edits not saved yet.</summary>
    AskChanged,

    /// <summary>Say it's gone, in a banner; the text stays, and saving writes it back.</summary>
    AskDeleted,
}

/// <summary>
/// What a change on disk does to an open file (DESIGN.md §18, "Project files", "When a file changes on disk"). Pure, so
/// it's tested on its own.
/// </summary>
public static class DiskChange
{
    /// <param name="read">The file as the editor last read or saved it; null when it was missing then.</param>
    /// <param name="onDisk">The file now; null when it's gone.</param>
    /// <param name="hasUnsavedEdits">The editor has edits not saved yet.</param>
    public static DiskChangeAction Decide(string? read, string? onDisk, bool hasUnsavedEdits)
    {
        if (string.Equals(read, onDisk, StringComparison.Ordinal))
        {
            return DiskChangeAction.None;
        }
        if (onDisk is null)
        {
            return DiskChangeAction.AskDeleted;
        }
        return hasUnsavedEdits ? DiskChangeAction.AskChanged : DiskChangeAction.Reload;
    }
}
