namespace Deventry.Core.FromClaudette;

/// <summary>
/// How Claudette told a thread its message went (DESIGN.md §2, "Renaming to Deventry"; §18, "Threads"), so a thread's
/// tool rows from before Deventry, reopened from History, still read as done rather than failed.
/// </summary>
public static class ClaudetteThreads
{
    public static bool IsDeliveryAnswer(string text) =>
        text.StartsWith("Claudette delivered this message to the sub-thread", StringComparison.Ordinal);
}
