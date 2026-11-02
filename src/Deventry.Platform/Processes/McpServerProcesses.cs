using Deventry.Core.Sessions;

namespace Deventry.Platform.Processes;

/// <summary>
/// A tab's MCP servers among its processes (DESIGN.md §4, "Cleanup"): Claude Code's own, started with it and stopped as
/// it exits, rather than something Claude ran. Each is a child of the tab's <c>claude</c> running the server's program
/// with its arguments, and whatever it started in turn is the server's too. The tools Claude runs are under a shell, so
/// never a child of <c>claude</c> itself.
/// </summary>
public static class McpServerProcesses
{
    /// <summary>Windows' extensions for a program, which a server's command may leave out: <c>node</c> runs <c>node.exe</c>.</summary>
    private static readonly string[] ProgramExtensions = [".exe", ".cmd", ".bat", ".com"];

    /// <summary>The PIDs of <paramref name="servers"/>' processes in a tab's tree, and of what they started.</summary>
    public static IReadOnlySet<int> Find(IReadOnlyList<ProcessSnapshot> tree, IReadOnlyList<McpServerLaunch> servers)
    {
        var found = new HashSet<int>();
        if (servers.Count == 0)
        {
            return found;
        }
        var roots = tree.Where(p => p.IsRoot).Select(p => p.Pid).ToHashSet();
        foreach (var process in tree.Where(p => !p.IsRoot && roots.Contains(p.ParentPid) && servers.Any(s => Runs(p, s))))
        {
            found.Add(process.Pid);
        }
        var children = tree.Where(p => !p.IsRoot).ToLookup(p => p.ParentPid);
        var under = new Queue<int>(found);
        while (under.TryDequeue(out var pid))
        {
            foreach (var child in children[pid].Where(c => found.Add(c.Pid)))
            {
                under.Enqueue(child.Pid);
            }
        }
        return found;
    }

    /// <summary>
    /// <paramref name="process"/> runs <paramref name="server"/>: the same program, and, when its command line is known,
    /// every one of the server's arguments in it, so a server run through <c>cmd</c> or <c>bash</c> isn't taken for
    /// another that is.
    /// </summary>
    private static bool Runs(ProcessSnapshot process, McpServerLaunch server) =>
        string.Equals(ProgramName(process.Name), ProgramName(server.Command), StringComparison.OrdinalIgnoreCase)
        && (process.CommandLine is not { } commandLine || server.Arguments.All(a => commandLine.Contains(a, StringComparison.Ordinal)));

    /// <summary>A program's file name without Windows' extension for it, from a path in either system's separators.</summary>
    private static string ProgramName(string command)
    {
        var name = command[(command.LastIndexOfAny(['/', '\\']) + 1)..];
        return ProgramExtensions.FirstOrDefault(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)) is { } extension ? name[..^extension.Length] : name;
    }
}
