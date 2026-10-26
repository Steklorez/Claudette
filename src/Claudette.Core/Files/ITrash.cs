namespace Claudette.Core.Files;

/// <summary>What moving a file or folder to the trash came to.</summary>
public enum TrashOutcome
{
    /// <summary>It's in the trash, where it can be put back from.</summary>
    Trashed,

    /// <summary>There's no trash it can go to (its drive has none, or the system has none Claudette can use): it's still there.</summary>
    NoTrash,

    /// <summary>It couldn't be moved, being in use, not Claudette's to move or gone: it's still there, if it was.</summary>
    Failed,
}

/// <summary>What moving to the trash came to, and why not when it didn't.</summary>
/// <param name="Reason">The system's words for why not, or null.</param>
public sealed record TrashResult(TrashOutcome Outcome, string? Reason = null)
{
    public static TrashResult Trashed { get; } = new(TrashOutcome.Trashed);
}

/// <summary>
/// The OS's trash, for deleting from the Project Files tab's explorer (DESIGN.md §18, "Project files", "Renaming and
/// deleting"): Windows' Recycle Bin, the macOS Trash, or the desktop's trash on Linux. <c>Claudette.Platform</c> has each.
/// </summary>
public interface ITrash
{
    /// <summary>Its name, as a sentence says it: "the Recycle Bin", "the Trash", "the trash".</summary>
    string Name { get; }

    /// <summary>
    /// Moves the file or folder to the trash: a link itself, not what it leads to. It's never deleted for good instead:
    /// where it can't go to the trash, it stays, and the result says so.
    /// </summary>
    Task<TrashResult> MoveToTrashAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>A system Claudette has no trash for: everything stays, and deleting asks to delete it for good.</summary>
public sealed class NoTrash(string reason = "Claudette has no trash to use here.") : ITrash
{
    public string Name => "the trash";

    public Task<TrashResult> MoveToTrashAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TrashResult(TrashOutcome.NoTrash, reason));
}
