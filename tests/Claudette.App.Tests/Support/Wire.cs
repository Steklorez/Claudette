using System.Text.Json.Nodes;

namespace Claudette.App.Tests;

/// <summary>Stream-json lines in the shape Claude Code 2.1.284 sends them.</summary>
internal static class Wire
{
    public static string Init() =>
        """{"type":"system","subtype":"init","session_id":"s1","model":"claude-opus-5-5","permissionMode":"default"}""";

    public static JsonObject AgentBlock(string id, string description, string prompt, string? type = null, bool background = false)
    {
        var input = new JsonObject { ["description"] = description, ["prompt"] = prompt };
        if (type is not null)
        {
            input["subagent_type"] = type;
        }
        if (background)
        {
            input["run_in_background"] = true;
        }
        return new JsonObject { ["type"] = "tool_use", ["id"] = id, ["name"] = "Agent", ["input"] = input };
    }

    public static JsonObject Message(params JsonObject[] blocks) =>
        new() { ["role"] = "assistant", ["content"] = new JsonArray(blocks.Select(b => (JsonNode?)b).ToArray()) };

    public static string Agent(string id, string description, string prompt, string type = "general-purpose", string? parent = null, bool background = false) =>
        Assistant(parent, AgentBlock(id, description, prompt, type, background));

    public static string Tool(string? parent, string id, string name, JsonObject input) =>
        Assistant(parent, new JsonObject { ["type"] = "tool_use", ["id"] = id, ["name"] = name, ["input"] = input });

    public static string Text(string? parent, string text) =>
        Assistant(parent, new JsonObject { ["type"] = "text", ["text"] = text });

    private static string Assistant(string? parent, JsonObject block) => new JsonObject
    {
        ["type"] = "assistant",
        ["message"] = Message(block),
        ["parent_tool_use_id"] = parent,
    }.ToJsonString();

    public static string TaskStarted(string taskId, string toolUseId, bool backgrounded = false, int depth = 1) => new JsonObject
    {
        ["type"] = "system", ["subtype"] = "task_started", ["task_id"] = taskId, ["tool_use_id"] = toolUseId,
        ["description"] = "d", ["subagent_type"] = "general-purpose", ["is_backgrounded"] = backgrounded, ["spawn_depth"] = depth, ["task_type"] = "local_agent",
    }.ToJsonString();

    public static string Notification(string taskId, string toolUseId, string status, string summary, long? tokens = null, int? toolUses = null, long? durationMs = null)
    {
        var message = new JsonObject
        {
            ["type"] = "system", ["subtype"] = "task_notification", ["task_id"] = taskId, ["tool_use_id"] = toolUseId,
            ["status"] = status, ["output_file"] = "", ["summary"] = summary,
        };
        if (tokens is not null)
        {
            message["usage"] = new JsonObject { ["total_tokens"] = tokens, ["tool_uses"] = toolUses ?? 0, ["duration_ms"] = durationMs ?? 0 };
        }
        return message.ToJsonString();
    }

    public static string Updated(string taskId, string status) => new JsonObject
    {
        ["type"] = "system", ["subtype"] = "task_updated", ["task_id"] = taskId, ["patch"] = new JsonObject { ["status"] = status },
    }.ToJsonString();

    public static JsonObject ResultMessage(string toolUseId, string text, bool isError = false) => new()
    {
        ["role"] = "user",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = toolUseId, ["content"] = text, ["is_error"] = isError }),
    };

    public static string Result(string toolUseId, string text, bool isError = false, string? parent = null, JsonNode? toolUseResult = null, bool interrupted = false)
    {
        var message = new JsonObject
        {
            ["type"] = "user",
            ["message"] = ResultMessage(toolUseId, text, isError),
            ["parent_tool_use_id"] = parent,
        };
        if (toolUseResult is not null)
        {
            message["tool_use_result"] = toolUseResult;
        }
        if (interrupted)
        {
            message["tool_result_meta"] = new JsonArray(new JsonObject { ["id"] = toolUseId, ["non_execution_kind"] = "interrupted" });
        }
        return message.ToJsonString();
    }

    /// <summary>The frame Claude Code puts around a subagent's report in the tool result text.</summary>
    public static string HandBack(string report, string agentId) =>
        "[Subagent hand-back] The text below is the final report of a subagent this session delegated to. The report follows:\n"
        + string.Join('\n', report.Split('\n').Select(l => "  " + l))
        + $"\nagentId: {agentId} (use SendMessage with to: '{agentId}' to continue this agent)\n<usage>subagent_tokens: 1020\ntool_uses: 0\nduration_ms: 95</usage>";

    public static string CanUseTool(string requestId, string toolUseId, string command, string? agentId = null) => new JsonObject
    {
        ["type"] = "control_request",
        ["request_id"] = requestId,
        ["request"] = new JsonObject
        {
            ["subtype"] = "can_use_tool", ["tool_name"] = "Bash", ["input"] = new JsonObject { ["command"] = command },
            ["tool_use_id"] = toolUseId, ["agent_id"] = agentId,
        },
    }.ToJsonString();

    public static string TurnResult() =>
        """{"type":"result","subtype":"success","is_error":false,"result":"done","session_id":"s1","duration_ms":1000}""";

    /// <summary>A transcript entry.</summary>
    public static string Entry(string type, string timestamp, JsonObject message, JsonNode? toolUseResult = null, bool sidechain = false)
    {
        var entry = new JsonObject { ["type"] = type, ["timestamp"] = timestamp, ["isSidechain"] = sidechain, ["message"] = message, ["sessionId"] = "s1" };
        if (toolUseResult is not null)
        {
            entry["toolUseResult"] = toolUseResult;
        }
        return entry.ToJsonString();
    }
}
