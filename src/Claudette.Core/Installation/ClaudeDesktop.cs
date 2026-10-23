using System.Text.RegularExpressions;
using Claudette.Core.Diffs;
using Claudette.Core.ProjectTools;

namespace Claudette.Core.Installation;

/// <summary>Which of the Claude desktop app's files a path is, if any.</summary>
public enum ClaudeDesktopFile
{
    /// <summary>Not the desktop app's.</summary>
    None,

    /// <summary>The desktop app itself, or its <c>claude-desktop</c> command.</summary>
    App,

    /// <summary>The copy of Claude Code the desktop app keeps for its own use.</summary>
    ClaudeCodeCopy,
}

/// <summary>
/// The Claude desktop app, which people take for Claude Code (DESIGN.md §12, "Installing Claude Code"). It isn't: it keeps
/// a copy of Claude Code for its own use, in a folder per version that it replaces as it updates, and puts no
/// <c>claude</c> on <c>PATH</c>. Claudette never runs the app or its copy.
/// </summary>
/// <param name="paths">Where other programs keep their files; tests use temporary folders.</param>
public sealed partial class ClaudeDesktop(ProjectToolPaths paths, ToolOS os)
{
    /// <summary>The desktop app's package family on Windows: its data folder's name, and the end of its install folder's.</summary>
    private const string WindowsPackageFamily = "Claude_pzs8sxrjxfjjc";

    /// <summary>The desktop app's command on <c>PATH</c> (on Windows, its app execution alias).</summary>
    private string Command => os == ToolOS.Windows ? "claude-desktop.exe" : "claude-desktop";

    /// <summary>
    /// Whether the desktop app is installed: its <c>claude-desktop</c> command on <paramref name="searchPath"/> or in its
    /// usual place, its package's data folder on Windows, or <c>Claude.app</c> on macOS.
    /// </summary>
    public bool IsInstalled(string? searchPath)
    {
        if (os == ToolOS.MacOS)
        {
            return Directory.Exists(Path.Combine(paths.Applications, "Claude.app"))
                || Directory.Exists(Path.Combine(paths.Home, "Applications", "Claude.app"));
        }
        if (FileProbe.FindIn(searchPath, Command) is not null)
        {
            return true;
        }
        if (os == ToolOS.Linux)
        {
            return File.Exists("/usr/bin/claude-desktop");
        }
        return paths.LocalAppData is { } local
            && (File.Exists(Path.Combine(local, "Microsoft", "WindowsApps", Command))
                || Directory.Exists(Path.Combine(local, "Packages", WindowsPackageFamily))
                || Directory.Exists(Path.Combine(local, "AnthropicClaude")));
    }

    /// <summary>Whether <paramref name="path"/> is one of the desktop app's files, and which.</summary>
    public ClaudeDesktopFile Identify(string path)
    {
        var comparison = os.PathComparison();
        var normalized = path.Replace('\\', '/');
        if (CopyFolders().Any(folder => normalized.StartsWith(folder, comparison)))
        {
            return ClaudeDesktopFile.ClaudeCodeCopy;
        }
        if (os == ToolOS.Windows && WindowsPackageFolder().Match(normalized) is { Success: true } package)
        {
            // Its package's data folder holds its copy of Claude Code, under LocalCache/Roaming/Claude/claude-code.
            return normalized[(package.Index + package.Length)..].StartsWith("LocalCache/Roaming/Claude/claude-code/", StringComparison.OrdinalIgnoreCase)
                ? ClaudeDesktopFile.ClaudeCodeCopy
                : ClaudeDesktopFile.App;
        }
        var name = Path.GetFileName(normalized);
        return string.Equals(name, Command, comparison)
            || os == ToolOS.MacOS && normalized.Contains("/Claude.app/", comparison)
            || os == ToolOS.Windows && paths.LocalAppData is { } local && normalized.StartsWith(Folder(local, "AnthropicClaude"), comparison)
            ? ClaudeDesktopFile.App
            : ClaudeDesktopFile.None;
    }

    /// <summary>Where the desktop app keeps its copy of Claude Code when it isn't a Windows package: its Electron data folder's <c>claude-code</c>.</summary>
    private IEnumerable<string> CopyFolders()
    {
        switch (os)
        {
            case ToolOS.Windows:
                if (paths.AppData is { } roaming)
                {
                    yield return Folder(roaming, "Claude", "claude-code");
                }
                break;
            case ToolOS.MacOS:
                yield return Folder(paths.Home, "Library", "Application Support", "Claude", "claude-code");
                break;
            default:
                yield return Folder(paths.Home, ".config", "Claude", "claude-code");
                break;
        }
    }

    private static string Folder(string root, params string[] parts) =>
        string.Join('/', [root.Replace('\\', '/').TrimEnd('/'), .. parts]) + "/";

    /// <summary>
    /// A folder of the desktop app's Windows package, its publisher's id at the end: its install folder
    /// (<c>WindowsApps/Claude_3.6556.2.0_x64__pzs8sxrjxfjjc/</c>), its data folder (<c>Packages/Claude_pzs8sxrjxfjjc/</c>) or
    /// its aliases' (<c>WindowsApps/Claude_pzs8sxrjxfjjc/</c>). Claude Code's own installs are never a package named Claude.
    /// </summary>
    [GeneratedRegex(@"/(WindowsApps|Packages)/Claude_([^/]*_)?[a-z0-9]{13}/", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsPackageFolder();
}
