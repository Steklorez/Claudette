using Deventry.Core.Sessions;
using Deventry.Platform.Processes;

namespace Deventry.Platform.Tests.Processes;

/// <summary>A tab's MCP servers among its processes, which closing it doesn't ask about (DESIGN.md §4, "Cleanup").</summary>
public sealed class McpServerProcessesTests
{
    private const int Claude = 100;

    [Fact]
    public void A_child_of_claude_running_a_servers_program_is_the_server_and_so_is_what_it_started()
    {
        var tree = Tree(
            Process(1, "relay_win.exe", @"C:\Users\me\.unity/relay\relay_win.exe --mcp"),
            Process(2, "unity-bridge.exe", "unity-bridge.exe --port 6400", parent: 1),
            Process(3, "helper.exe", "helper.exe", parent: 2),
            Process(4, "bash.exe", "bash.exe -c -l \"npm run dev\""));

        var servers = McpServerProcesses.Find(tree, [new McpServerLaunch(@"C:\Users\me\.unity/relay\relay_win.exe", ["--mcp"])]);

        Assert.Equal([1, 2, 3], servers.Order());
    }

    [Fact]
    public void A_command_without_its_extension_or_its_folder_names_the_same_program()
    {
        var tree = Tree(Process(1, "node.exe", "\"C:\\Program Files\\nodejs\\node.exe\" server.js"), Process(2, "uvx", "/home/me/.local/bin/uvx mcp-git"));

        Assert.Equal([1], McpServerProcesses.Find(tree, [new McpServerLaunch("node", ["server.js"])]));
        Assert.Equal([2], McpServerProcesses.Find(tree, [new McpServerLaunch("/home/me/.local/bin/uvx", ["mcp-git"])]));
    }

    [Fact]
    public void The_same_program_with_other_arguments_is_something_else_such_as_the_shell_Claude_runs_its_commands_in()
    {
        var tree = Tree(Process(1, "bash", "bash -c serve-tickets"), Process(2, "bash", "bash -c -l \"npm test\""));

        Assert.Equal([1], McpServerProcesses.Find(tree, [new McpServerLaunch("bash", ["-c", "serve-tickets"])]));
    }

    [Fact]
    public void A_program_Claude_ran_under_its_shell_isnt_a_server_whatever_it_runs()
    {
        var tree = Tree(Process(1, "bash.exe", "bash.exe -c \"node server.js\""), Process(2, "node.exe", "node server.js", parent: 1));

        Assert.Empty(McpServerProcesses.Find(tree, [new McpServerLaunch("node", ["server.js"])]));
    }

    [Fact]
    public void Without_command_lines_a_server_is_known_by_its_program_and_with_no_servers_nothing_is()
    {
        var tree = Tree(Process(1, "relay_win.exe", commandLine: null), Process(2, "vite", commandLine: null));

        Assert.Equal([1], McpServerProcesses.Find(tree, [new McpServerLaunch("relay_win.exe", ["--mcp"])]));
        Assert.Empty(McpServerProcesses.Find(tree, []));
    }

    private static ProcessSnapshot[] Tree(params ProcessSnapshot[] children) =>
        [new ProcessSnapshot { Pid = Claude, ParentPid = 1, Name = "claude.exe", IsRoot = true }, .. children];

    private static ProcessSnapshot Process(int pid, string name, string? commandLine, int parent = Claude) =>
        new() { Pid = pid, ParentPid = parent, Name = name, CommandLine = commandLine };
}
