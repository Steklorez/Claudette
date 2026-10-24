using Claudette.Core.Diffs;
using Claudette.Core.Settings;

namespace Claudette.Core.ProjectTools;

/// <summary>Where other programs keep their files, injected so tests use temporary folders (DESIGN.md §15).</summary>
/// <param name="Home">The user's home folder: <c>~</c>.</param>
/// <param name="AppData"><c>%APPDATA%</c> on Windows.</param>
/// <param name="LocalAppData"><c>%LOCALAPPDATA%</c> on Windows.</param>
/// <param name="ProgramData"><c>%ProgramData%</c> on Windows, where the Epic launcher lists its installs.</param>
/// <param name="ProgramFiles"><c>%ProgramFiles%</c> on Windows.</param>
/// <param name="Applications">macOS's <c>/Applications</c>.</param>
public sealed record ProjectToolPaths(string Home, string? AppData = null, string? LocalAppData = null, string? ProgramData = null, string? ProgramFiles = null, string Applications = "/Applications")
{
    public static ProjectToolPaths ForCurrentUser()
    {
        static string? Folder(Environment.SpecialFolder folder)
        {
            var path = Environment.GetFolderPath(folder);
            return path.Length > 0 ? path : null;
        }
        return new ProjectToolPaths(
            Folder(Environment.SpecialFolder.UserProfile) ?? "",
            Folder(Environment.SpecialFolder.ApplicationData),
            Folder(Environment.SpecialFolder.LocalApplicationData),
            Folder(Environment.SpecialFolder.CommonApplicationData),
            Folder(Environment.SpecialFolder.ProgramFiles));
    }
}

/// <summary>What's remembered about a project on this machine: its configuration choice, a chosen engine folder.</summary>
public interface IProjectMemory
{
    /// <summary>The value kept under <paramref name="key"/> for the project at <paramref name="projectPath"/>, or null.</summary>
    string? Get(string projectPath, string key);
}

/// <summary>A memory that remembers nothing.</summary>
public sealed class NoProjectMemory : IProjectMemory
{
    public static NoProjectMemory Instance { get; } = new();

    public string? Get(string projectPath, string key) => null;
}

/// <summary>
/// Everything a project type needs besides the folder: the OS, the settings, other programs' files, the machine's
/// memory. A type's own services, such as Unreal's registry entries, are its own, given to it when it's made.
/// </summary>
public sealed class ProjectToolContext
{
    public required ToolOS OS { get; init; }

    public required ProjectToolSettings Settings { get; init; }

    public required ProjectToolPaths Paths { get; init; }

    public IProjectMemory Memory { get; init; } = NoProjectMemory.Instance;

    /// <summary>Folders the search for projects never looks into: every type's, and the shared ones (<see cref="ProjectSearch"/>).</summary>
    public IReadOnlySet<string> SkippedFolders { get; init; } = ProjectSearch.SkippedFolders;

    /// <summary>Running processes, to tell whether an editor has the project open. Null can't tell, and doesn't guess.</summary>
    public ISystemProcesses? Processes { get; init; }

    public IFileProbe Probe { get; init; } = FileProbe.Instance;

    /// <summary>The user's shell (<c>$SHELL</c>) for custom actions on macOS and Linux; null uses <c>/bin/sh</c>.</summary>
    public string? Shell { get; init; }

    /// <summary>The name of Perforce's config file (<c>P4CONFIG</c>), which marks a workspace's root like <c>.git</c>.</summary>
    public string? P4ConfigName { get; init; }

    /// <summary>Where jobs can write files for Claudette to read afterwards, such as Unity's test results. In Claudette's data folder.</summary>
    public string? JobsDirectory { get; init; }
}
