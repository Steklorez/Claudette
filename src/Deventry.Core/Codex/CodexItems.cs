using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;

namespace Deventry.Core.Codex;

/// <summary>One call a Codex item makes: its id, its name as the conversation labels it, its input, and what it does.</summary>
public sealed record CodexCall(string Id, string Name, JsonObject Input, ToolCall Call);

/// <summary>A call's outcome: its text, whether it failed, and a command's exit code.</summary>
public sealed record CodexCallResult(string Id, string Text, bool IsError, int? ExitCode = null);

/// <summary>
/// Codex's items (<c>ThreadItem</c>, DESIGN.md §18, "Codex, a second agent") as tool calls: a command, by what Codex says it
/// does (<c>commandActions</c>); a file change, a call per file; an MCP tool, a web search, an image looked at, a tool
/// Deventry runs. Text, reasoning and the user's own messages aren't calls.
/// </summary>
public static partial class CodexItems
{
    /// <summary>The labels Codex's calls are shown with, which aren't Claude Code's tools' names.</summary>
    public const string Shell = "Shell";
    public const string ReadFile = "Read file";
    public const string Search = "Search";
    public const string ListFiles = "List files";
    public const string CreateFile = "Create file";
    public const string EditFile = "Edit file";
    public const string DeleteFile = "Delete file";
    public const string WebSearch = "Web search";
    public const string ViewImage = "View image";

    /// <summary>The calls an item makes; none for an item that isn't a call.</summary>
    public static IReadOnlyList<CodexCall> CallsOf(JsonObject item)
    {
        var id = item.GetString("id") ?? "";
        switch (item.GetString("type"))
        {
            case "commandExecution":
                return [CommandCall(id, item)];
            case "fileChange":
                var changes = item.GetArray("changes")?.OfType<JsonObject>().ToArray() ?? [];
                return [.. changes.Select((change, index) => FileCall(changes.Length == 1 ? id : $"{id}:{index}", change))];
            case "mcpToolCall":
                var server = item.GetString("server") ?? "";
                var tool = item.GetString("tool") ?? "";
                return [new CodexCall(id, $"mcp__{server}__{tool}", item.GetObject("arguments") ?? [], new ToolCall(ToolKind.Mcp, $"mcp__{server}__{tool}") { McpServer = server, McpTool = tool })];
            case "webSearch":
                var query = item.GetString("query") ?? item.GetObject("action")?.GetString("query");
                return [new CodexCall(id, WebSearch, Input("query", query), new ToolCall(ToolKind.WebSearch, WebSearch) { Query = query })];
            case "imageView":
                var image = item.GetString("path");
                return [new CodexCall(id, ViewImage, Input("file_path", image), new ToolCall(ToolKind.ViewImage, ViewImage) { Path = image })];
            case "dynamicToolCall" when item.GetString("tool") == CodexTools.SendMessage:
                // A thread's message to a sub-thread, shown as Claude Code's SendMessage is (DESIGN.md §18, "Threads").
                return [new CodexCall(id, Threads.SendMessageCall.ToolName, item.GetObject("arguments") ?? [], new ToolCall(ToolKind.Other, Threads.SendMessageCall.ToolName))];
            case "dynamicToolCall":
                var name = item.GetString("tool") ?? "tool";
                return [new CodexCall(id, name, item.GetObject("arguments") ?? [], new ToolCall(ToolKind.Other, name))];
            default:
                return [];
        }
    }

    /// <summary>The outcome of each call a finished item made.</summary>
    public static IReadOnlyList<CodexCallResult> ResultsOf(JsonObject item)
    {
        var id = item.GetString("id") ?? "";
        var status = item.GetString("status");
        var failed = status is "failed" or "declined";
        switch (item.GetString("type"))
        {
            case "commandExecution":
                var exitCode = item["exitCode"] is JsonValue code && code.TryGetValue<int>(out var value) ? value : (int?)null;
                return [new CodexCallResult(id, item.GetString("aggregatedOutput") ?? "", failed || exitCode is not (null or 0), exitCode)];
            case "fileChange":
                var count = item.GetArray("changes")?.Count ?? 0;
                return [.. Enumerable.Range(0, count).Select(index => new CodexCallResult(count == 1 ? id : $"{id}:{index}", failed ? $"The change was {status}." : "", failed))];
            case "mcpToolCall":
                if (item.GetObject("error")?.GetString("message") is { } error)
                {
                    return [new CodexCallResult(id, error, true)];
                }
                return [new CodexCallResult(id, ContentText(item.GetObject("result")?.GetArray("content")), failed)];
            case "dynamicToolCall":
                return [new CodexCallResult(id, ContentText(item.GetArray("contentItems")), failed || item.GetBool("success") == false)];
            case "webSearch" or "imageView":
                return [new CodexCallResult(id, "", failed)];
            default:
                return [];
        }
    }

    private static CodexCall CommandCall(string id, JsonObject item)
    {
        var actions = item.GetArray("commandActions")?.OfType<JsonObject>().ToArray() ?? [];
        // What the user would have typed: the actions' own commands, else the command without the shell Codex wraps it in.
        var command = actions.Length > 0 && actions.All(a => a.GetString("command") is { Length: > 0 })
            ? string.Join(" && ", actions.Select(a => a.GetString("command")))
            : Unwrap(item.GetString("command") ?? "");
        var input = new JsonObject { ["command"] = command };
        if (item.GetString("cwd") is { } cwd)
        {
            input["cwd"] = cwd;
        }
        if (actions is [var only])
        {
            switch (only.GetString("type"))
            {
                case "read":
                    var path = only.GetString("path");
                    return new CodexCall(id, ReadFile, input, new ToolCall(ToolKind.Read, ReadFile) { Path = path, Command = command });
                case "search":
                    return new CodexCall(id, Search, input, new ToolCall(ToolKind.Search, Search) { Pattern = only.GetString("query"), Where = only.GetString("path"), Command = command });
                case "listFiles":
                    return new CodexCall(id, ListFiles, input, new ToolCall(ToolKind.Find, ListFiles) { Where = only.GetString("path"), Command = command });
            }
        }
        return new CodexCall(id, Shell, input, new ToolCall(ToolKind.Command, Shell) { Command = command });
    }

    private static CodexCall FileCall(string id, JsonObject change)
    {
        var path = change.GetString("path");
        var diff = change.GetString("diff");
        var kind = change.GetObject("kind")?.GetString("type") ?? change.GetString("kind");
        var input = Input("file_path", path);
        return kind switch
        {
            "add" => new CodexCall(id, CreateFile, input, new ToolCall(ToolKind.Write, CreateFile) { Path = path, Content = diff }),
            "delete" => new CodexCall(id, DeleteFile, input, new ToolCall(ToolKind.Delete, DeleteFile) { Path = path, Content = diff }),
            _ => new CodexCall(id, EditFile, input, new ToolCall(ToolKind.Edit, EditFile)
            {
                Path = path,
                Hunks = diff,
                MovedTo = change.GetObject("kind")?.GetString("move_path"),
            }),
        };
    }

    [GeneratedRegex("""^\s*(?:"[^"]*[\\/](?:powershell|pwsh)(?:\.exe)?"|(?:\S*[\\/])?(?:powershell|pwsh)(?:\.exe)?)\s+(?:-NoProfile\s+)?-Command\s+(?<body>.+)$""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PowerShellWrapper();

    [GeneratedRegex("""^\s*(?:\S*[\\/])?(?:bash|zsh|sh)\s+-l?c\s+(?<body>.+)$""", RegexOptions.Singleline)]
    private static partial Regex PosixWrapper();

    /// <summary>A command without the shell Codex runs it in: <c>"…powershell.exe" -Command 'git status'</c> is <c>git status</c>.</summary>
    public static string Unwrap(string command)
    {
        if (PowerShellWrapper().Match(command) is { Success: true } powershell)
        {
            return Unquote(powershell.Groups["body"].Value.Trim());
        }
        if (PosixWrapper().Match(command) is { Success: true } posix)
        {
            return Unquote(posix.Groups["body"].Value.Trim());
        }
        return command;
    }

    private static string Unquote(string text) =>
        text.Length >= 2 && (text[0] == '\'' && text[^1] == '\'' || text[0] == '"' && text[^1] == '"') ? text[1..^1].Replace("''", "'", StringComparison.Ordinal) : text;

    private static JsonObject Input(string key, string? value) => value is null ? [] : new JsonObject { [key] = value };

    private static string ContentText(JsonArray? content) =>
        string.Join("\n", content?.OfType<JsonObject>().Select(c => c.GetString("text")).OfType<string>() ?? []);
}
