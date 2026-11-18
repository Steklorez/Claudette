using Deventry.Core.Settings;

namespace Deventry.Core.Subagents;

/// <summary>
/// What Deventry tells Claude about subagents, as Settings → Tabs → Subagents or the tab's own setting says
/// (DESIGN.md §5, "Subagents"): the note Claude Code starts with, the note on the next message when the setting changes
/// in a running tab, and the answer to an <c>Agent</c> call Deventry's hook refuses.
/// </summary>
public static class SubagentMessages
{
    /// <summary>The tools that start a subagent, as a hook's matcher: <c>Agent</c>, and <c>Task</c>, its older name.</summary>
    public const string ToolMatcher = "Agent|Task";

    /// <summary>Whether a tool call starts a subagent.</summary>
    public static bool StartsSubagent(string? toolName) => toolName is "Agent" or "Task";

    /// <summary>
    /// The note Claude Code starts with (<c>--append-system-prompt</c>), which lasts through compaction; null when Claude
    /// decides, as Claude Code has it.
    /// </summary>
    /// <param name="tool">The tool that starts a subagent, as the agent knows it: Claude Code's Agent tool, Codex's spawn_agent.</param>
    public static string? StartNote(SubagentUse use, string tool = ClaudeTool) => use switch
    {
        SubagentUse.Discourage =>
            "The user prefers that you don't delegate to subagents. Do the work yourself, in this conversation, with your own tools, "
            + $"rather than calling {tool}. Use a subagent only when the user asks for one.",
        SubagentUse.DontAllow =>
            $"Subagents are turned off for this session: don't call {tool}, because Deventry refuses every call. "
            + "Do the work yourself, in this conversation, with your own tools. If the user asks for a subagent, "
            + "tell them subagents are turned off in Deventry's settings.",
        _ => null,
    };

    /// <summary>Claude Code's tool that starts a subagent, as the notes name it.</summary>
    public const string ClaudeTool = "the Agent tool";

    /// <summary>Codex's tool that starts a subagent (DESIGN.md §18, "Subagents").</summary>
    public const string CodexTool = "spawn_agent";

    /// <summary>The note on the user's next message after the tab's setting changed while its Claude Code ran.</summary>
    public static string ChangeNote(SubagentUse use, string tool = ClaudeTool) => use switch
    {
        SubagentUse.Discourage => "[Deventry] From now on, the user prefers that you don't delegate to subagents. "
            + "Do the work yourself, in this conversation, with your own tools, and use a subagent only when the user asks for one.",
        SubagentUse.DontAllow => $"[Deventry] Subagents are now turned off: don't call {tool}, because Deventry refuses every call. "
            + "Do the work yourself, in this conversation, with your own tools.",
        _ => $"[Deventry] Subagents are allowed again: use {tool} whenever handing work to a subagent helps.",
    };

    /// <summary>The line under a message that carried <see cref="ChangeNote"/>, with the note as its tip.</summary>
    /// <param name="agent">What the tab calls its agent: "Claude", "Codex".</param>
    public static string ChangeNoteLine(SubagentUse use, string agent = "Claude") => use switch
    {
        SubagentUse.Discourage => $"Told {agent} to avoid subagents",
        SubagentUse.DontAllow => $"Told {agent} it can't use subagents",
        _ => $"Told {agent} it can use subagents again",
    };

    /// <summary>
    /// What Claude reads as the result of an <c>Agent</c> call the hook refused. Claude Code puts its own words before it
    /// ("PreToolUse:Agent hook error: …"), so the conversation finds the call by this text.
    /// </summary>
    public const string Refused =
        "Deventry didn't start this subagent: subagents are turned off for this tab in Deventry's settings. "
        + "Do this work yourself, in this conversation, with your own tools.";

    /// <summary>Whether a tool call's result is the hook's refusal.</summary>
    public static bool IsRefusal(string? resultText) => resultText?.Contains(Refused, StringComparison.Ordinal) == true;
}
