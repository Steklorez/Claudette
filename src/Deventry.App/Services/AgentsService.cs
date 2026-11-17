using Deventry.Core.Agents;
using Deventry.Core.Settings;

namespace Deventry.App.Services;

/// <summary>
/// The agents a tab can run (DESIGN.md §18, "Codex, a second agent"), as <c>ProjectToolsService.AllTypes</c> lists the
/// project types: Claude Code, the one Deventry is made for and every tab's default, then Codex. A folder remembers the
/// agent its last new tab used.
/// </summary>
public static class AgentsService
{
    public static IReadOnlyList<IAgentType> AllTypes { get; } = [ClaudeCodeAgent.Instance, CodexAgent.Instance];

    /// <summary>The agent of a kind; Claude Code for null, or a kind this Deventry doesn't know.</summary>
    public static IAgentType Find(string? kind) => AllTypes.FirstOrDefault(a => a.Kind == kind) ?? ClaudeCodeAgent.Instance;

    /// <summary>The kind a tab's state keeps: null for Claude Code, so a state saved before agents reads the same.</summary>
    public static string? StateKind(IAgentType agent) => agent.Kind == AgentKinds.ClaudeCode ? null : agent.Kind;

    /// <summary>The agent a new tab in <paramref name="folder"/> starts with: the one its last new tab used.</summary>
    public static IAgentType ForFolder(AppState state, string folder) =>
        Find(state.FolderAgents.FirstOrDefault(e => FolderHistory.SamePath(e.Key, folder)).Value);

    /// <summary>Remembers <paramref name="agent"/> for <paramref name="folder"/>'s next new tab.</summary>
    public static void Remember(AppState state, string folder, IAgentType agent)
    {
        foreach (var path in state.FolderAgents.Keys.Where(p => FolderHistory.SamePath(p, folder)).ToList())
        {
            state.FolderAgents.Remove(path);
        }
        state.FolderAgents[folder] = agent.Kind;
    }
}
