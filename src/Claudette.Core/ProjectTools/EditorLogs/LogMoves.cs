namespace Claudette.Core.ProjectTools.EditorLogs;

/// <summary>Lines in a read of a log that say the log moved to another file.</summary>
/// <param name="Line">
/// The last of the read's lines that says so, which is still this log's: the lines after it may be another writer's.
/// -1 when the move was said in an earlier read.
/// </param>
/// <param name="To">The file it moved to, a full path in the OS's form.</param>
public readonly record struct LogMoveLine(int Line, string To);

/// <summary>
/// Reads where a log says it moved to (DESIGN.md §18, "Editor logs", "A log that moves"), fed each read's lines in order,
/// since what a line says may be on the line after it. Each log being read has its own.
/// </summary>
public abstract class LogMoveReader
{
    /// <summary>
    /// The moves a read's lines say, in order. <paramref name="fresh"/> when they start the log afresh, and
    /// <paramref name="final"/> when no more will come, so a move waiting for its next line is decided without it. A read
    /// with no lines still counts: a move can wait at most one read for the line after it.
    /// </summary>
    public abstract IReadOnlyList<LogMoveLine> Read(IReadOnlyList<string> lines, bool fresh, bool final);
}
