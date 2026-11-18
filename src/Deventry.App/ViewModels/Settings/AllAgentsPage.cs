using Deventry.Core.Settings;

namespace Deventry.App.ViewModels.Settings;

/// <summary>All agents' Subagents choices, and the tab's own in Tab settings… (DESIGN.md §5, "Subagents").</summary>
public sealed record SubagentUseChoice(SubagentUse Use)
{
    public static string Label(SubagentUse use) => use switch
    {
        SubagentUse.Discourage => "Discourage them",
        SubagentUse.DontAllow => "Don't allow them",
        _ => "The agent decides",
    };

    public override string ToString() => Label(Use);
}

/// <summary>
/// Settings → Agents → All agents (DESIGN.md §18, "Codex, a second agent"): what applies to Claude Code's and Codex's
/// tabs alike: whether they use subagents, and offering the other agent at a usage limit. Each agent has a page of its
/// own after it (<see cref="ClaudeCodePage"/>, <see cref="CodexPage"/>).
/// </summary>
public sealed class AllAgentsPage : SettingsPage
{
    public AllAgentsPage(SettingsContext context) : base(context, SettingsCategory.AllAgents)
    {
        Section("subagents", "Subagents",
        [
            Setting("claudeCode.subagents", "Subagents", s => s.ClaudeCode.Subagents, (s, v) => s.ClaudeCode.Subagents = v,
                "Whether the agent hands parts of its work to subagents.",
                "Discourage asks it to do the work itself, unless you ask for a subagent. Don't allow tells it it can't, and Deventry turns down any subagent it starts anyway, in tabs already open too.",
                ["agents", "task tool", "spawn agent"], TabOverrideKeys.Subagents),
        ], description: "Each tab can change this for itself, from Tab settings… in its menu.");
        Section("otherAgent", "At a usage limit",
        [
            Setting("codex.offerOtherAgentAtLimit", "Offer to continue in the other agent", s => s.Codex.OfferOtherAgentAtLimit,
                (s, v) => s.Codex.OfferOtherAgentAtLimit = v,
                "A tab stopped at its plan's limit offers a tab of the other agent, which carries the work on.",
                "Its first message, a draft for you to send, says what you asked, the to-do list, the files changed so far and the last reply.",
                ["limit", "continue", "hand off", "claude code", "codex"]),
        ]);
    }

    public override string IconKey => "IconToolAgent";

    public override string Description => "What applies to every agent's tabs.";

    public IReadOnlyList<SubagentUseChoice> SubagentChoices { get; } =
        [new(SubagentUse.ClaudeDecides), new(SubagentUse.Discourage), new(SubagentUse.DontAllow)];

    /// <summary>
    /// <b>Subagents</b> (DESIGN.md §5, "Subagents"): whether the agent may hand work to them. Applies at once, in tabs
    /// already open too; each tab can override it in Tab settings….
    /// </summary>
    public SubagentUseChoice Subagents
    {
        get => SubagentChoices.FirstOrDefault(c => c.Use == Settings.ClaudeCode.Subagents) ?? SubagentChoices[0];
        set => Set(value?.Use ?? SubagentUse.ClaudeDecides, v => Settings.ClaudeCode.Subagents = v);
    }

    /// <summary><b>Offer to continue in the other agent</b> (DESIGN.md §18, "Continuing in the other agent").</summary>
    public bool OfferOtherAgentAtLimit
    {
        get => Settings.Codex.OfferOtherAgentAtLimit;
        set => Set(value, v => Settings.Codex.OfferOtherAgentAtLimit = v);
    }
}
