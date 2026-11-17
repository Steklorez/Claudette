namespace Deventry.Core.Agents;

/// <summary>
/// Codex, OpenAI's coding agent, Deventry's second (DESIGN.md §18, "Codex, a second agent"), driven through
/// <c>codex app-server</c>. It has none of what only Claude Code has.
/// </summary>
public sealed class CodexAgent : IAgentType
{
    public static CodexAgent Instance { get; } = new();

    private CodexAgent()
    {
    }

    public string Kind => AgentKinds.Codex;

    public string Name => "Codex";

    public string ShortName => "Codex";

    public AgentFeatures Features => AgentFeatures.None;
}
