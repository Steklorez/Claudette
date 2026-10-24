namespace Claudette.Core.Library;

/// <summary>
/// How the library's copy of a session's transcript stands to this machine's (DESIGN.md §9, "One machine at a time"). A
/// transcript only grows, so one that is the start of the other has nothing the other lacks.
/// </summary>
public enum LibraryStanding
{
    /// <summary>The library has no copy of the transcript.</summary>
    Missing,

    /// <summary>The library's copy holds the same lines as this machine's.</summary>
    Same,

    /// <summary>The library's copy is the start of this machine's: this machine has turns to add to it.</summary>
    Behind,

    /// <summary>This machine's transcript is the start of the library's: another machine carried the session on.</summary>
    Ahead,

    /// <summary>Each has turns the other doesn't: the session went on here and on another machine.</summary>
    Diverged,
}
