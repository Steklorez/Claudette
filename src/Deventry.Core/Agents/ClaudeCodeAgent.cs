namespace Deventry.Core.Agents;

/// <summary>Claude Code, Deventry's first agent and the one it's made for: the default for a new tab.</summary>
public sealed class ClaudeCodeAgent : IAgentType
{
    public static ClaudeCodeAgent Instance { get; } = new();

    private ClaudeCodeAgent()
    {
    }

    public string Kind => AgentKinds.ClaudeCode;

    public string Name => "Claude Code";

    public string ShortName => "Claude";

    public AgentFeatures Features =>
        AgentFeatures.RemoteControl | AgentFeatures.UsageCredits | AgentFeatures.Ultracode | AgentFeatures.OutputStyles
        | AgentFeatures.PromptSuggestions | AgentFeatures.PromptCacheTimer | AgentFeatures.ClaudeDocs | AgentFeatures.SessionMessaging
        | AgentFeatures.GeneratedTitles;
}
