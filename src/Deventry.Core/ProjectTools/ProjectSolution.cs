namespace Deventry.Core.ProjectTools;

/// <summary>
/// A project's own <c>solution</c> in <c>deventry.json</c> or <c>deventry.local.json</c> (DESIGN.md §18, "deventry.json"):
/// the file that defines it, usually its solution, which the project's menu offers to open. A solution opens with the IDE
/// Settings → Project tools → All tools says, the OS's app by default; any other file with the OS's app.
/// </summary>
/// <param name="Path">As the file has it: from the project's folder (<c>NightOwl.sln</c>), or a full path.</param>
/// <param name="Scope">The file it's in. The local file's wins.</param>
public sealed record ProjectSolution(string Path, ProjectFileScope Scope)
{
    /// <summary>The action's id, which a run of it and the main action's shortcut know it by.</summary>
    public const string ActionId = "file:solution";

    /// <summary>The files that are solutions or workspaces, which open with the IDE Settings names.</summary>
    public static IReadOnlySet<string> SolutionExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".sln", ".slnx", ".code-workspace", ".xcworkspace",
    };

    /// <summary>What the file is called, for the menu's entry: <c>NightOwl.sln</c>.</summary>
    public string FileName => Path.Replace('\\', '/').TrimEnd('/') is var path ? path[(path.LastIndexOf('/') + 1)..] : Path;

    /// <summary>A solution or a workspace, by its extension, rather than another file that defines the project.</summary>
    public bool IsSolution => SolutionExtensions.Contains(System.IO.Path.GetExtension(FileName));

    /// <summary>
    /// Where the file is, read from <paramref name="folder"/>, or why it can't be opened: not a path, on a network share,
    /// which isn't looked at by itself (as an icon's isn't), or not there. The reason follows the path: "isn't there".
    /// </summary>
    public (string? FullPath, string? Problem) Resolve(string folder)
    {
        const string network = "is on a network share, which Deventry doesn't look at by itself";
        if (Protocol.ReplyFileReference.IsNetworkPath(Path))
        {
            return (null, network);
        }
        string full;
        try
        {
            full = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, Path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, "isn't a path");
        }
        if (Protocol.ReplyFileReference.IsNetworkPath(full))
        {
            return (null, network);
        }
        // A workspace such as Xcode's is a folder.
        return File.Exists(full) || Directory.Exists(full) ? (full, null) : (null, "isn't there");
    }

    /// <summary>
    /// The project menu's entry, <b>Open NightOwl.sln</b>: opened with the IDE for a solution, else the OS's app; disabled
    /// with why while the file can't be opened.
    /// </summary>
    /// <param name="fileName">The name of the file that names it, for the tip: <c>deventry.json</c>.</param>
    public ProjectAction ToAction(string folder, string fileName)
    {
        var (full, problem) = Resolve(folder);
        return new ProjectAction(ActionId, $"Open {FileName}", ProjectActionKind.Open)
        {
            OpenPath = full,
            OpenWithIde = IsSolution,
            Description = IsSolution
                ? $"The project's solution, from {fileName}: opens with the app Settings → Project tools → All tools says"
                : $"The file that defines the project, from {fileName}: opens with the app your system uses for it",
            DisabledReason = problem is null ? null : $"{fileName}'s solution, {Path}, {problem}.",
        };
    }
}
