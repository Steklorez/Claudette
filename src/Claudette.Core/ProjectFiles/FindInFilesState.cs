namespace Claudette.Core.ProjectFiles;

/// <summary>
/// What Find in files had last, which it opens with next time (DESIGN.md §18, "Project files", "Finding in files"): kept
/// in <c>state.json</c>, so it lasts from one run of Claudette to the next.
/// </summary>
public sealed class FindInFilesState
{
    public string Query { get; set; } = "";

    public bool MatchCase { get; set; }

    public bool WholeWords { get; set; }

    public bool Regex { get; set; }

    public string Replacement { get; set; } = "";

    /// <summary>The <b>File mask</b>, as typed.</summary>
    public string FileMask { get; set; } = "";

    /// <summary>The <b>Directory</b> looked in, or null for <b>In project</b>. It applies again only to a project it's in.</summary>
    public string? Directory { get; set; }
}
