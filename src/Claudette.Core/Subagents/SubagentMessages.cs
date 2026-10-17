using Claudette.Core.Settings;

namespace Claudette.Core.Subagents;

/// <summary>
/// What Claudette tells Claude about subagents, as Settings → Claude Code → Subagents or the tab's own setting says
/// (DESIGN.md §5, "Subagents"): the note Claude Code starts with, the note on the next message when the setting changes
/// in a running tab, and the answer to an <c>Agent</c> call Claudette's hook refuses.
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
    public static string? StartNote(SubagentUse use) => use switch
    {
        SubagentUse.Discourage =>
            "The user prefers that you don't delegate to subagents. Do the work yourself, in this conversation, with your own tools, "
            + "rather than calling the Agent tool. Use a subagent only when the user asks for one.",
        SubagentUse.DontAllow =>
            "Subagents are turned off for this session: don't call the Agent tool, because Claudette refuses every call. "
            + "Do the work yourself, in this conversation, with your own tools. If the user asks for a subagent, "
            + "tell them subagents are turned off in Claudette's settings.",
        _ => null,
    };

    /// <summary>The note on the user's next message after the tab's setting changed while its Claude Code ran.</summary>
    public static string ChangeNote(SubagentUse use) => use switch
    {
        SubagentUse.Discourage => "[Claudette] From now on, the user prefers that you don't delegate to subagents. "
            + "Do the work yourself, in this conversation, with your own tools, and use a subagent only when the user asks for one.",
        SubagentUse.DontAllow => "[Claudette] Subagents are now turned off: don't call the Agent tool, because Claudette refuses every call. "
            + "Do the work yourself, in this conversation, with your own tools.",
        _ => "[Claudette] Subagents are allowed again: use the Agent tool whenever handing work to a subagent helps.",
    };

    /// <summary>The line under a message that carried <see cref="ChangeNote"/>, with the note as its tip.</summary>
    public static string ChangeNoteLine(SubagentUse use) => use switch
    {
        SubagentUse.Discourage => "Told Claude to avoid subagents",
        SubagentUse.DontAllow => "Told Claude it can't use subagents",
        _ => "Told Claude it can use subagents again",
    };

    /// <summary>
    /// What Claude reads as the result of an <c>Agent</c> call the hook refused. Claude Code puts its own words before it
    /// ("PreToolUse:Agent hook error: …"), so the conversation finds the call by this text.
    /// </summary>
    public const string Refused =
        "Claudette didn't start this subagent: subagents are turned off for this tab in Claudette's settings. "
        + "Do this work yourself, in this conversation, with your own tools.";

    /// <summary>Whether a tool call's result is the hook's refusal.</summary>
    public static bool IsRefusal(string? resultText) => resultText?.Contains(Refused, StringComparison.Ordinal) == true;
}
