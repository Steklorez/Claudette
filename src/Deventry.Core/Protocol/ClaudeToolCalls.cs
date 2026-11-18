using System.Globalization;
using System.Text.Json.Nodes;
using Deventry.Core.Sessions;

namespace Deventry.Core.Protocol;

/// <summary>Claude Code's tool calls as <see cref="ToolCall"/>s: its tools' names and their inputs' fields.</summary>
public static class ClaudeToolCalls
{
    /// <summary>The kind of Claude Code's tool <paramref name="name"/>; an MCP tool by its <c>mcp__</c> prefix.</summary>
    public static ToolKind KindOf(string name) => name switch
    {
        "Bash" or "PowerShell" => ToolKind.Command,
        "BashOutput" or "KillShell" or "KillBash" => ToolKind.CommandOutput,
        "Monitor" => ToolKind.Monitor,
        "Read" or "NotebookRead" => ToolKind.Read,
        "Edit" or "MultiEdit" or "NotebookEdit" => ToolKind.Edit,
        "Write" => ToolKind.Write,
        "Grep" => ToolKind.Search,
        "Glob" or "LS" => ToolKind.Find,
        "WebFetch" => ToolKind.WebFetch,
        "WebSearch" => ToolKind.WebSearch,
        "Agent" or "Task" => ToolKind.Agent,
        "TodoWrite" or "TaskCreate" or "TaskUpdate" or "TaskList" or "TaskGet" => ToolKind.Todo,
        "Skill" => ToolKind.Skill,
        "AskUserQuestion" => ToolKind.Question,
        "ExitPlanMode" or "EnterPlanMode" => ToolKind.Plan,
        _ when name.StartsWith("mcp__", StringComparison.Ordinal) => ToolKind.Mcp,
        _ => ToolKind.Other,
    };

    /// <summary>The call Claude Code made to <paramref name="name"/> with <paramref name="input"/>.</summary>
    public static ToolCall From(string name, JsonObject input)
    {
        var kind = KindOf(name);
        var call = new ToolCall(kind, name)
        {
            Path = input.GetString("file_path") ?? input.GetString("notebook_path"),
            Description = input.GetString("description"),
        };
        return kind switch
        {
            ToolKind.Command => call with { Command = input.GetString("command") },
            ToolKind.Read => call with { FromLine = Line(input["offset"]) },
            ToolKind.Edit => call with { OldText = input.GetString("old_string"), NewText = input.GetString("new_string") },
            ToolKind.Write => call with { Content = input.GetString("content") },
            ToolKind.Search or ToolKind.Find => call with { Pattern = input.GetString("pattern"), Where = input.GetString("path") },
            ToolKind.WebFetch => call with { Url = input.GetString("url"), Prompt = input.GetString("prompt") },
            ToolKind.WebSearch => call with { Query = input.GetString("query") },
            ToolKind.Agent => call with { Prompt = input.GetString("prompt"), SubagentType = input.GetString("subagent_type") },
            ToolKind.Skill => call with { Skill = input.GetString("skill") },
            ToolKind.Mcp => McpOf(call, name),
            _ => call,
        };
    }

    private static ToolCall McpOf(ToolCall call, string name)
    {
        // mcp__<server>__<tool>; a server's name can itself hold underscores, never two together.
        var rest = name["mcp__".Length..];
        var split = rest.IndexOf("__", StringComparison.Ordinal);
        return split < 0 ? call with { McpServer = rest } : call with { McpServer = rest[..split], McpTool = rest[(split + 2)..] };
    }

    private static int? Line(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<int>(out var line) => line,
        JsonValue value when value.TryGetValue<double>(out var line) => (int)line,
        JsonValue value when value.TryGetValue<string>(out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line) => line,
        _ => null,
    };
}
