namespace Deventry.Core.Agents;

/// <summary>
/// A kind of agent a tab can run, as <see cref="ProjectTools.IProjectType"/> is a kind of project (DESIGN.md §18, "Codex, a
/// second agent"): Claude Code, the one Deventry is made for, and Codex. A tab keeps its agent; a session can't move
/// between them.
/// </summary>
public interface IAgentType
{
    /// <summary>A stable id, such as <c>claude-code</c>: what a tab's state and a folder remember (<see cref="AgentKinds"/>).</summary>
    string Kind { get; }

    /// <summary>"Claude Code": its page in Settings, the <b>New tab</b> menu and the info card.</summary>
    string Name { get; }

    /// <summary>
    /// What the agent is called mid-sentence, for the model behind it: "Claude", in <i>Stopping Claude in Grappling
    /// hook</i> and <i>Claude is working</i>.
    /// </summary>
    string ShortName { get; }

    /// <summary>What only some agents have, which a tab of this agent shows (<see cref="AgentFeatures"/>).</summary>
    AgentFeatures Features { get; }
}

/// <summary>The agents' <see cref="IAgentType.Kind"/>s, as words, so a kind a later Deventry adds reads as unknown rather than failing.</summary>
public static class AgentKinds
{
    public const string ClaudeCode = "claude-code";

    public const string Codex = "codex";
}

/// <summary>
/// What a tab shows only when its agent has it (DESIGN.md §18, "Codex, a second agent", "Hidden on a Codex tab"). The
/// session's own interfaces (<see cref="Sessions.IRewindsFiles"/> and the rest) say what it can do; these say what the
/// tab offers before a session has started.
/// </summary>
[Flags]
public enum AgentFeatures
{
    None = 0,

    /// <summary>Remote Control, the Claude app's link to a session (DESIGN.md §18).</summary>
    RemoteControl = 1 << 0,

    /// <summary>Usage credits paying past a plan limit (DESIGN.md §6).</summary>
    UsageCredits = 1 << 1,

    /// <summary>Ultracode, Claude's own workflows of subagents (DESIGN.md §5, "Model and effort").</summary>
    Ultracode = 1 << 2,

    /// <summary>Claude Code's output styles (DESIGN.md §5).</summary>
    OutputStyles = 1 << 3,

    /// <summary>The next prompt the agent predicts (DESIGN.md §5, "Prompt suggestions").</summary>
    PromptSuggestions = 1 << 4,

    /// <summary>The prompt cache's countdown, which follows Anthropic's cache lifetimes (DESIGN.md §6, "Prompt cache").</summary>
    PromptCacheTimer = 1 << 5,

    /// <summary>Docs Claude writes with claude.ai's Docs connector (DESIGN.md §5, "Claude Docs").</summary>
    ClaudeDocs = 1 << 6,

    /// <summary>Other sessions on this machine, which the composer's <c>@</c> names (DESIGN.md §5, "Autocomplete").</summary>
    SessionMessaging = 1 << 7,

    /// <summary>A session title the agent writes after the first prompt (DESIGN.md §13, "Session naming").</summary>
    GeneratedTitles = 1 << 8,
}
