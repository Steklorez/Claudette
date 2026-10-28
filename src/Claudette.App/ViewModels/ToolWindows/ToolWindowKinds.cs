using Claudette.App.Conversation;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// The tool windows Claudette has (DESIGN.md §3, "Tool windows"), in the order they take along their edge at first: the
/// side panel's pages, which all dock on the right. A feature adds its kind to <see cref="All"/> and its view to
/// <c>Views/ToolWindows/ToolWindowViews</c>.
/// </summary>
public static class ToolWindowKinds
{
    /// <summary>The files changed in the session (DESIGN.md §8).</summary>
    public static readonly ToolWindowKind Files = new()
    {
        Id = "Files",
        Title = "Changed files",
        PaletteText = "Show changed files",
        FollowsSession = true,
    };

    /// <summary>The agent map (DESIGN.md §18, "Agent map").</summary>
    public static readonly ToolWindowKind Agents = new()
    {
        Id = "Agents",
        Title = "Agents",
        PaletteText = "Show the agent map",
        FollowsSession = true,
    };

    /// <summary>The project's details, for a tab with project tools (DESIGN.md §18, "Project tools").</summary>
    public static readonly ToolWindowKind Project = new()
    {
        Id = "Project",
        Title = "Project",
        FollowsSession = true,
        ShowsWhen = new(t => t.ProjectTools, nameof(ProjectToolsViewModel.HasTools), t => t.ProjectTools.HasTools),
    };

    /// <summary>The session's processes, while the process monitor is on (DESIGN.md §4, "Process monitor").</summary>
    public static readonly ToolWindowKind Processes = new()
    {
        Id = "Processes",
        Title = "Processes",
        PaletteText = "Show processes",
        FollowsSession = true,
        ShowsWhen = new(t => t.ProcessMonitor, nameof(ProcessMonitorViewModel.IsOn), t => t.ProcessMonitor.IsOn),
    };

    /// <summary>The plan, the tasks and the Claude Docs, once the session has any (DESIGN.md §5, "Tasks").</summary>
    public static readonly ToolWindowKind Tasks = new()
    {
        Id = "Tasks",
        Title = "Tasks",
        PaletteText = "Show tasks",
        FollowsSession = true,
        ShowsWhen = new(t => t.TodoList, nameof(TodoList.HasAnything), t => t.TodoList.HasAnything),
    };

    /// <summary>The session's MCP servers, when it has any (DESIGN.md §4, "MCP servers").</summary>
    public static readonly ToolWindowKind Mcp = new()
    {
        Id = "Mcp",
        Title = "MCP",
        PaletteText = "Show MCP servers",
        FollowsSession = true,
        ShowsWhen = new(t => t, nameof(TabViewModel.HasMcpServers), t => t.HasMcpServers),
    };

    /// <summary>The project's scratch pad (DESIGN.md §18, "Scratch pad").</summary>
    public static readonly ToolWindowKind ScratchPad = new()
    {
        Id = "ScratchPad",
        Title = "Scratch pad",
        PaletteText = "Show scratch pad",
        FollowsSession = true,
    };

    /// <summary>
    /// The game against Claudette, while one is open (DESIGN.md §18, "Games with Claudette"). The app's, not a tab's: the
    /// shell gives it the game.
    /// </summary>
    public static readonly ToolWindowKind Games = new()
    {
        Id = "Games",
        Title = "Games",
    };

    /// <summary>Every kind, in the order the windows take along their edge at first.</summary>
    public static IReadOnlyList<ToolWindowKind> All { get; } = [Files, Agents, Project, Processes, Tasks, Mcp, ScratchPad, Games];

    /// <summary>The kind named <paramref name="id"/>, or null for a name no kind has (one taken out since it was saved).</summary>
    public static ToolWindowKind? Find(string id) => All.FirstOrDefault(k => k.Id == id);
}
