using System.Text.Json.Nodes;

namespace Deventry.Core.Codex;

/// <summary>A call Codex made to one of Deventry's own tools (<c>item/tool/call</c>), which the tab answers.</summary>
/// <param name="Tool">The tool's name, such as <see cref="CodexTools.SendMessage"/>.</param>
/// <param name="ThreadId">The thread that made it: the tab's own, or one of its subagents'.</param>
public sealed record CodexToolCall(string Tool, JsonObject Arguments, string? ThreadId);

/// <summary>The answer to a <see cref="CodexToolCall"/>: what Codex reads, and whether the tool did what was asked.</summary>
public sealed record CodexToolAnswer(string Text, bool Success);

/// <summary>
/// Deventry's own tools, which every Codex thread it starts has (DESIGN.md §18, "Threads"): given as dynamic tools at
/// the thread's start, which Codex keeps with the thread and restores on resume, and answered by the tab, as Claude
/// Code's <c>SendMessage</c> is answered by Deventry's hook.
/// </summary>
public static class CodexTools
{
    /// <summary>A thread's message to one of its sub-threads.</summary>
    public const string SendMessage = "deventry_send_message";

    /// <summary>Every tool, as <c>thread/start</c>'s <c>dynamicTools</c> takes them.</summary>
    public static JsonArray All() => [SendMessageSpec()];

    private static JsonObject SendMessageSpec() => new()
    {
        ["type"] = "function",
        ["name"] = SendMessage,
        ["description"] = "Hand work to one of this thread's sub-threads: another Deventry tab, each a session of its own, which Deventry names. "
            + "Deventry delivers the message as that tab's next turn, and sends you every sub-thread's result in one message once they've all finished. "
            + "Use it only when Deventry has said this tab is a thread.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["to"] = new JsonObject { ["type"] = "string", ["description"] = "The sub-thread's name, as Deventry gave it." },
                ["message"] = new JsonObject { ["type"] = "string", ["description"] = "The work to hand over, complete enough to do without asking." },
            },
            ["required"] = new JsonArray("to", "message"),
            ["additionalProperties"] = false,
        },
    };
}
